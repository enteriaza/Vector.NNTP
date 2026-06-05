// RabbitMqConnectionFactory.Connection.cs -- async connection attempt with structured logging.
//
// Separated from the main RabbitMqConnectionFactory.cs to isolate the async state machine and exception handling from the
// synchronous factory configuration and endpoint construction.
//
// Flow:
//   1. CreateConnectionAsync (RabbitMqConnectionFactory.cs) captures the Stopwatch timestamp and delegates here.
//   2. The RabbitMQ client library's CreateConnectionAsync tries each AmqpTcpEndpoint in order.
//   3. On success: log negotiated parameters, attach lifecycle event handlers, return the connection.
//   4. On cancellation: log at Information (expected during host shutdown), rethrow.
//   5. On failure: log at Error with elapsed time, rethrow for caller's exponential back-off retry loop.
//
// Caller:
//   RabbitMqConnectionFactory.cs -- CreateConnectionAsync delegates to ConnectWithLoggingAsync after constructing
//   the factory and endpoint list.
//   ConnectionPool.StartAsync -- retry loop with exponential back-off (2s base, 30s cap, 1s jitter).
//
// Resource safety:
//   If AttachConnectionEventHandlers throws after a successful connect, the open IConnection is disposed via
//   DisposalUtilities.TryDispose before the exception propagates -- preventing an orphaned TCP socket, heartbeat
//   timer, and automatic recovery thread.  The disposal exception (if any) is intentionally discarded because
//   the original AttachConnectionEventHandlers exception is the actionable failure.
//
// Security:
//   No method in this file logs credentials, connection strings, or broker passwords.  LogConnected logs only
//   negotiated transport parameters (endpoint, frame max, channel max, heartbeat, elapsed time).
//   LogConnectionCancelled and LogConnectionFailed log only the pre-formatted endpoint summary and elapsed time.
//   The endpoint summary is produced by FormattingUtilities.FormatEndpointSummary which emits only host:port
//   pairs -- no credential material.
//
// Cross-platform:
//   Fully portable.  All APIs used (Stopwatch.GetElapsedTime, IConnection, DisposalUtilities.TryDispose,
//   Task, ConfigureAwait) are part of the .NET Base Class Library and behave identically on Windows (x64) and
//   Linux (x64) on .NET 8.  No P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.
//
// SIMD applicability:
//   Not applicable.  This file contains a single async method that awaits a connection attempt and logs the
//   outcome.  There are no contiguous memory buffers, byte-level pattern searches, or bulk numeric operations
//   that would benefit from vector instructions.

using System.Diagnostics;
using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.Utilities.Disposal;
using RabbitMQ.Client;

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Async connection attempt with structured success, cancellation, and failure logging for
    /// <see cref="RabbitMqConnectionFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Responsibility:</b> Contains <see cref="ConnectWithLoggingAsync"/> which wraps the RabbitMQ client
    /// library's <see cref="ConnectionFactory.CreateConnectionAsync(IEnumerable{AmqpTcpEndpoint}, CancellationToken)"/>
    /// call with structured logging for all three outcomes: success (negotiated parameters), cancellation (expected
    /// during host shutdown), and failure (all endpoints unreachable).</para>
    ///
    /// <para><b>Caller:</b> <see cref="CreateConnectionAsync"/> in <c>RabbitMqConnectionFactory.cs</c> delegates here after
    /// constructing the <see cref="ConnectionFactory"/> and <see cref="AmqpTcpEndpoint"/> list.  The
    /// <see cref="Stopwatch.GetTimestamp"/> is captured <em>after</em> factory construction so the elapsed time
    /// reflects only the async TCP/AMQP handshake.</para>
    ///
    /// <para><b>Resource safety:</b> If <see cref="AttachConnectionEventHandlers"/> throws after a successful connect,
    /// the open <see cref="IConnection"/> is disposed via <see cref="DisposalUtilities.TryDispose"/> before the
    /// exception propagates -- preventing an orphaned TCP socket, heartbeat timer, and automatic recovery thread from
    /// leaking.  Synchronous disposal is used because the connection is in a partially-initialised state where the
    /// underlying resources may already be faulted -- consistent with the pattern in
    /// <c>ConnectionPool.dispose pooled connections</c>.</para>
    ///
    /// <para><b>Security:</b> No method in this file logs credentials, connection strings, or broker passwords.
    /// <see cref="LogConnected"/> logs only negotiated transport parameters.  <see cref="LogConnectionCancelled"/>
    /// and <see cref="LogConnectionFailed"/> log only the pre-formatted endpoint summary (host:port pairs) and
    /// elapsed time.</para>
    ///
    /// <para><b>Thread safety:</b> Called exactly once per application lifetime by
    /// <see cref="ConnectionPool.StartAsync"/>.  All mutable state (the connection being established) is
    /// local to this method's async state machine.  The injected logger is thread-safe by contract.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  All APIs used are BCL types available on all .NET 8 runtimes
    /// (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  No data processing or vectorisable computation
    /// paths.</para>
    /// </remarks>
    public sealed partial class RabbitMqConnectionFactory
    {

        #region Private Methods -- Connection Attempt

        /// <summary>
        /// Initiates the asynchronous connection attempt and wraps it with structured logging for both success and failure
        /// outcomes.
        /// </summary>
        /// <remarks>
        /// <para>This method is intentionally <c>async</c> (rather than returning the bare
        /// <see cref="Task{IConnection}"/>) so that the success/failure logging executes <em>after</em> the connection
        /// attempt completes, not when the caller awaits.</para>
        ///
        /// <para><b>Success path:</b></para>
        /// <list type="number">
        ///   <item><description>Await
        ///     <see cref="ConnectionFactory.CreateConnectionAsync(IEnumerable{AmqpTcpEndpoint}, CancellationToken)"/>
        ///     which tries each endpoint in declaration order.</description></item>
        ///   <item><description>Log the negotiated endpoint parameters and elapsed time via
        ///     <see cref="LogConnected"/>.</description></item>
        ///   <item><description>Subscribe to all <see cref="IConnection"/> lifecycle events via
        ///     <see cref="AttachConnectionEventHandlers"/>.  If subscription fails, the open connection is disposed via
        ///     <see cref="DisposalUtilities.TryDispose"/> before the exception propagates -- preventing an orphaned TCP
        ///     connection, heartbeat timer, and automatic recovery thread from leaking.  The disposal exception (if any) is
        ///     intentionally discarded because the original <see cref="AttachConnectionEventHandlers"/> exception is the
        ///     actionable failure.</description></item>
        ///   <item><description>Return the fully-initialised connection to the caller.</description></item>
        /// </list>
        ///
        /// <para><b>Cancellation path:</b> <see cref="OperationCanceledException"/> is caught separately and logged at
        /// <see cref="LogLevel.Information"/> -- cancellation during host shutdown is expected behaviour, not an error.
        /// The exception is rethrown to propagate to <see cref="ConnectionPool.StartAsync"/>.  No
        /// <c>when (cancellationToken.IsCancellationRequested)</c> filter is applied because any
        /// <see cref="OperationCanceledException"/> -- whether triggered by the caller's token or by the RabbitMQ client
        /// library's internal timeout -- is a cancellation, not a connection failure.</para>
        ///
        /// <para><b>Failure path:</b> All other exceptions (typically
        /// <see cref="RabbitMQ.Client.Exceptions.BrokerUnreachableException"/>) are logged at
        /// <see cref="LogLevel.Error"/> and rethrown so the caller (<see cref="ConnectionPool.StartAsync"/>) can
        /// apply exponential back-off and retry.</para>
        ///
        /// <para><b>Elapsed time measurement:</b> A local function <c>ElapsedMs()</c> captures the
        /// <paramref name="connectStart"/> timestamp and computes elapsed milliseconds via
        /// <c>Stopwatch.GetElapsedTime</c>.  This is evaluated at the point of each log call -- not eagerly -- so
        /// the measurement reflects the actual time of the success, cancellation, or failure event.</para>
        /// </remarks>
        /// <param name="factory">Configured connection factory.</param>
        /// <param name="endpoints">Ordered broker endpoints to try.</param>
        /// <param name="options">Validated RabbitMQ options -- captured by event handler closures for log message context
        /// (e.g., the configured <see cref="RabbitMQOptions.NetworkRecoveryIntervalSeconds"/> in shutdown and recovery error
        /// handlers).</param>
        /// <param name="endpointSummary">Pre-formatted endpoint string for log messages.</param>
        /// <param name="connectStart"><see cref="Stopwatch.GetTimestamp"/> captured after factory construction so the
        /// elapsed time reflects only the async TCP/AMQP handshake, not synchronous configuration overhead.</param>
        /// <param name="cancellationToken">Cancellation token forwarded to the RabbitMQ client library.  Cancelled when
        /// the host is shutting down or the startup timeout expires.</param>
        /// <returns>An open <see cref="IConnection"/> with automatic recovery enabled and lifecycle event handlers
        /// attached.</returns>
        /// <exception cref="RabbitMQ.Client.Exceptions.BrokerUnreachableException">All configured endpoints failed to
        /// connect.  Logged at <see cref="LogLevel.Error"/> before rethrowing.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled or the
        /// RabbitMQ client library aborted the attempt internally.  Logged at <see cref="LogLevel.Information"/> before
        /// rethrowing.</exception>
        private async Task<IConnection> ConnectWithLoggingAsync(
            ConnectionFactory factory, List<AmqpTcpEndpoint> endpoints, RabbitMQOptions options,
            string endpointSummary, long connectStart, CancellationToken cancellationToken)
        {
            double ElapsedMs()
            {
                return Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds;
            }
            try
            {
                IConnection connection = await factory
                    .CreateConnectionAsync(endpoints, cancellationToken)
                    .ConfigureAwait(false);
                LogConnected(connection.Endpoint?.ToString() ?? "unknown", connection.FrameMax,
                    connection.ChannelMax, (ushort)connection.Heartbeat.TotalSeconds, ElapsedMs());
                try
                {
                    AttachConnectionEventHandlers(connection, options);
                }
                catch
                {
                    // The connection is open but event handler subscription failed.  Dispose via DisposalUtilities
                    // to prevent an orphaned TCP connection, heartbeat timer, and automatic recovery thread from
                    // leaking.  Best-effort -- the connection may already be in a faulted state.  The disposal
                    // exception (if any) is intentionally discarded; the original exception is the actionable failure.
                    _ = DisposalUtilities.TryDispose(connection);
                    throw;
                }
                return connection;
            }
            catch (OperationCanceledException)
            {
                LogConnectionCancelled(endpointSummary, ElapsedMs());
                throw;
            }
            catch (Exception ex)
            {
                LogConnectionFailed(ex, endpointSummary, ElapsedMs());
                throw;
            }
        }

        #endregion

    }
}
