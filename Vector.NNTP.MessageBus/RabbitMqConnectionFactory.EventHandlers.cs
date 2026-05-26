// RabbitMqConnectionFactory.EventHandlers.cs -- IConnection lifecycle event handler subscriptions.
//
// Subscribes to all IConnection events after a successful connect:
//   ConnectionShutdownAsync              -- application and broker-initiated disconnects
//   CallbackExceptionAsync              -- unhandled exceptions in consumer callbacks
//   ConnectionBlockedAsync              -- broker flow-control activation
//   ConnectionUnblockedAsync            -- broker flow-control release
//   RecoverySucceededAsync              -- automatic recovery success
//   ConnectionRecoveryErrorAsync        -- automatic recovery failure
//   ConsumerTagChangeAfterRecoveryAsync -- consumer tag reassignment after topology recovery
//
// Caller:
//   RabbitMqConnectionFactory.Connection.cs -- ConnectWithLoggingAsync, immediately after a successful
//   CreateConnectionAsync and before the connection is returned to ConnectionPool.
//
// Exception safety:
//   Every handler body is wrapped in try/catch to prevent exceptions from propagating into the RabbitMQ client
//   library's internal I/O thread event dispatch loop.  An unhandled exception there would tear down the
//   connection's internal state machine with no user-visible diagnostic -- far worse than a missed log line.
//
//   Handlers are split into two categories based on failure severity:
//
//   1. Logging-only handlers (shutdown, blocked, unblocked, callback exception, consumer tag changed):
//      Exceptions are swallowed and counted via _swallowedEventHandlerErrors.  The counter is the last-resort
//      observability mechanism -- it can be inspected via a health check or debugger when all else fails.
//
//   2. State-critical handlers (recovery succeeded, recovery error):
//      The critical state mutations (counter reset, counter increment, StopApplication) are isolated OUTSIDE the
//      try/catch that wraps the logging calls.  This ensures that even if the logger is broken (sink exception,
//      OOM, ObjectDisposedException during shutdown), the fail-fast mechanism continues to function.  Exceptions
//      from the state mutations themselves (Interlocked, Volatile, StopApplication) would indicate a catastrophic
//      runtime failure (e.g., access violation) that should propagate -- silently swallowing a failed
//      StopApplication call would produce the exact zombie scenario the fail-fast mechanism exists to prevent.
//
// Security:
//   No handler logs credentials, connection strings, or broker passwords.  Only AMQP reply codes/text, initiator
//   enums, flow-control reasons, endpoint strings, connection names, consumer tags, and recovery intervals are
//   included in log output.  The CallbackExceptionAsync handler iterates args.Detail which contains only
//   callback-context metadata (e.g., "context=BasicDeliver") -- no credential material.
//
// Cross-platform:
//   Fully portable.  All APIs used (IConnection event subscriptions, FormattingUtilities.FormatKeyValuePairs,
//   Task.CompletedTask) are part of the .NET Base Class Library and behave identically on Windows (x64) and
//   Linux (x64) on .NET 8.  No P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.
//
// SIMD applicability:
//   Not applicable.  This file contains only event handler subscriptions that perform string formatting and
//   [LoggerMessage] calls.  There are no contiguous memory buffers, byte-level pattern searches, or bulk
//   numeric operations that would benefit from vector instructions.

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Utilities;
using RabbitMQ.Client;

namespace Vector.NNTP.MessageBus
{
    /// <summary>
    /// <see cref="IConnection"/> lifecycle event handler subscriptions for <see cref="RabbitMqConnectionFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Responsibility:</b> Subscribes to all <see cref="IConnection"/> lifecycle events after a successful
    /// connection so that broker-initiated state changes (shutdown, flow-control, recovery, consumer tag reassignment)
    /// are logged for the lifetime of the singleton connection.  Called by <see cref="ConnectWithLoggingAsync"/> in
    /// <c>RabbitMqConnectionFactory.Connection.cs</c> immediately after
    /// <see cref="ConnectionFactory.CreateConnectionAsync(IEnumerable{AmqpTcpEndpoint}, CancellationToken)"/>
    /// succeeds.</para>
    ///
    /// <para><b>Exception safety:</b> Every handler body is wrapped in a <c>try</c>/<c>catch</c> that swallows
    /// exceptions from logging calls and increments <see cref="_swallowedEventHandlerErrors"/> as a last-resort
    /// observability signal.  These handlers execute on the RabbitMQ client library's internal I/O thread -- an
    /// unhandled exception would propagate into the library's event dispatch loop, potentially corrupting the
    /// connection's internal state machine with no user-visible diagnostic.</para>
    ///
    /// <para><b>State-critical handlers:</b> The <see cref="IConnection.RecoverySucceededAsync"/> and
    /// <see cref="IConnection.ConnectionRecoveryErrorAsync"/> handlers contain state mutations
    /// (<see cref="_consecutiveRecoveryFailures"/> counter reset/increment,
    /// <see cref="IHostApplicationLifetime.StopApplication"/>) that are isolated <em>outside</em> the logging
    /// try/catch.  This ensures the fail-fast mechanism continues to function even when the logger is broken.</para>
    ///
    /// <para><b>Thread safety:</b> All handlers execute on the client library's internal I/O thread.  The handlers
    /// only call <c>[LoggerMessage]</c> source-generated methods which are thread-safe by contract
    /// (<see cref="ILogger"/> is thread-safe).  The <see cref="_swallowedEventHandlerErrors"/> counter uses
    /// <see cref="Interlocked.Increment(ref int)"/> for safe concurrent access.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  All APIs used are BCL types available on all .NET 8 runtimes
    /// (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  No data processing or vectorisable computation
    /// paths.</para>
    /// </remarks>
    public sealed partial class RabbitMqConnectionFactory
    {

        #region Private Methods -- Connection Lifecycle Events

        /// <summary>
        /// Subscribes to all <see cref="IConnection"/> lifecycle events so that broker-initiated state changes are logged
        /// for the lifetime of the singleton connection.
        /// </summary>
        /// <remarks>
        /// <para>RabbitMQ.Client v7.x uses <c>AsyncEventHandler&lt;T&gt;</c> for all connection events (returning
        /// <see cref="Task"/>).  Each handler is a <em>non-static</em> lambda that captures <c>this</c> (for the logger)
        /// and the <paramref name="options"/> reference for log message context (e.g., the configured recovery interval in
        /// the shutdown handler).  The options reference is captured once and shared across all handler closures -- no
        /// per-event allocation occurs after this method returns.</para>
        ///
        /// <para><b>Events subscribed:</b></para>
        /// <list type="bullet">
        ///   <item><description><see cref="IConnection.ConnectionShutdownAsync"/> -- application and
        ///     broker-initiated disconnects.</description></item>
        ///   <item><description><see cref="IConnection.CallbackExceptionAsync"/> -- unhandled exceptions in
        ///     consumer callbacks.</description></item>
        ///   <item><description><see cref="IConnection.ConnectionBlockedAsync"/> -- broker flow-control
        ///     activation.</description></item>
        ///   <item><description><see cref="IConnection.ConnectionUnblockedAsync"/> -- broker flow-control
        ///     release.</description></item>
        ///   <item><description><see cref="IConnection.RecoverySucceededAsync"/> -- automatic recovery
        ///     success.</description></item>
        ///   <item><description><see cref="IConnection.ConnectionRecoveryErrorAsync"/> -- automatic recovery
        ///     failure.</description></item>
        ///   <item><description><see cref="IConnection.ConsumerTagChangeAfterRecoveryAsync"/> -- consumer
        ///     tag reassignment after topology recovery.</description></item>
        /// </list>
        ///
        /// <para><b>Exception safety:</b> Every handler body is wrapped in a <c>try</c>/<c>catch</c> that swallows
        /// exceptions from logging calls and increments <see cref="_swallowedEventHandlerErrors"/> as a last-resort
        /// observability signal.  These handlers execute on the RabbitMQ client library's internal I/O thread -- an
        /// unhandled exception would propagate into the library's event dispatch loop, potentially corrupting the
        /// connection's internal state machine with no user-visible diagnostic.</para>
        ///
        /// <para><b>State-critical handlers:</b> The <see cref="IConnection.RecoverySucceededAsync"/> and
        /// <see cref="IConnection.ConnectionRecoveryErrorAsync"/> handlers contain state mutations
        /// (<see cref="_consecutiveRecoveryFailures"/> counter reset/increment, <see cref="IHostApplicationLifetime.StopApplication"/>)
        /// that are isolated <em>outside</em> the logging try/catch.  This ensures the fail-fast mechanism continues to
        /// function even when the logger is broken.  Exceptions from these state mutations (which would indicate a
        /// catastrophic runtime failure) are allowed to propagate rather than being silently swallowed -- a failed
        /// <see cref="IHostApplicationLifetime.StopApplication"/> call must not be hidden, as that would produce the exact
        /// zombie scenario the fail-fast mechanism exists to prevent.</para>
        ///
        /// <para><b>Thread safety:</b> All handlers execute on the client library's internal I/O thread.  The handlers
        /// only call <c>[LoggerMessage]</c> source-generated methods which are thread-safe by contract
        /// (<see cref="ILogger"/> is thread-safe).  The <see cref="_swallowedEventHandlerErrors"/> counter uses
        /// <see cref="Interlocked.Increment(ref int)"/> for safe concurrent access.</para>
        ///
        /// <para><b>Allocation:</b> Each lambda allocates a single closure object at subscription time (capturing
        /// <c>this</c> and <c>options</c>).  No per-event allocations occur during handler execution except in the
        /// <see cref="IConnection.CallbackExceptionAsync"/> handler, which builds a
        /// <see cref="System.Text.StringBuilder"/> via <see cref="FormattingUtilities.FormatKeyValuePairs"/> to format the
        /// callback detail dictionary -- this is acceptable because callback exceptions are error-level events that should
        /// be rare.</para>
        /// </remarks>
        /// <param name="connection">The open connection to instrument with lifecycle logging.</param>
        /// <param name="options">Validated RabbitMQ options -- captured by event handler closures for log message context
        /// (e.g., the configured <see cref="RabbitMQOptions.NetworkRecoveryIntervalSeconds"/> in shutdown and recovery error
        /// handlers).</param>
        private void AttachConnectionEventHandlers(IConnection connection, RabbitMQOptions options)
        {
            connection.ConnectionShutdownAsync += (sender, args) =>
            {
                try
                {
                    if (args.Initiator == ShutdownInitiator.Application)
                    {
                        LogShutdownApplication(args.ReplyCode, args.ReplyText);
                    }
                    else
                    {
                        LogShutdownBroker(args.Exception, args.Initiator.ToString(), args.ReplyCode, args.ReplyText, options.NetworkRecoveryIntervalSeconds);
                    }
                }
                catch
                {
                    _ = Interlocked.Increment(ref _swallowedEventHandlerErrors);
                }
                return Task.CompletedTask;
            };
            connection.CallbackExceptionAsync += (sender, args) =>
            {
                try
                {
                    LogCallbackException(args.Exception, FormattingUtilities.FormatKeyValuePairs(args.Detail));
                }
                catch
                {
                    _ = Interlocked.Increment(ref _swallowedEventHandlerErrors);
                }
                return Task.CompletedTask;
            };
            connection.ConnectionBlockedAsync += (sender, args) =>
            {
                try
                {
                    LogConnectionBlocked(args.Reason);
                }
                catch
                {
                    _ = Interlocked.Increment(ref _swallowedEventHandlerErrors);
                }
                return Task.CompletedTask;
            };
            connection.ConnectionUnblockedAsync += (sender, args) =>
            {
                try
                {
                    LogConnectionUnblocked();
                }
                catch
                {
                    _ = Interlocked.Increment(ref _swallowedEventHandlerErrors);
                }
                return Task.CompletedTask;
            };
            connection.RecoverySucceededAsync += (sender, args) =>
            {
                // State mutation FIRST -- must not be inside the logging try/catch.  If the counter reset fails
                // (catastrophic runtime failure), the exception must propagate rather than being silently swallowed.
                Volatile.Write(ref _consecutiveRecoveryFailures, 0);
                try
                {
                    IConnection? conn = sender as IConnection;
                    string endpoint = conn?.Endpoint?.ToString() ?? "unknown";
                    string connectionName = conn?.ClientProvidedName ?? "unnamed";
                    LogRecoverySucceeded(endpoint, connectionName);
                }
                catch
                {
                    _ = Interlocked.Increment(ref _swallowedEventHandlerErrors);
                }
                return Task.CompletedTask;
            };
            connection.ConnectionRecoveryErrorAsync += (sender, args) =>
            {
                // State mutation FIRST -- the Interlocked.Increment and StopApplication calls are intentionally
                // outside the logging try/catch.  If the fail-fast mechanism itself throws (catastrophic runtime
                // failure), that exception must propagate -- silently swallowing a failed StopApplication call
                // would produce the exact zombie scenario the fail-fast mechanism exists to prevent.
                int failures = Interlocked.Increment(ref _consecutiveRecoveryFailures);
                int threshold = options.MaxConsecutiveRecoveryFailures;
                try
                {
                    LogRecoveryFailed(args.Exception, options.NetworkRecoveryIntervalSeconds, failures);
                }
                catch
                {
                    _ = Interlocked.Increment(ref _swallowedEventHandlerErrors);
                }
                // Threshold check is outside the logging try/catch -- StopApplication must execute even if
                // LogRecoveryFailed threw.  The == comparison ensures StopApplication fires exactly once.
                if (threshold > 0 && failures == threshold)
                {
                    try
                    {
                        LogRecoveryFatal(threshold, options.NetworkRecoveryIntervalSeconds);
                    }
                    catch
                    {
                        _ = Interlocked.Increment(ref _swallowedEventHandlerErrors);
                    }
                    hostLifetime.StopApplication();
                }
                return Task.CompletedTask;
            };
            connection.ConsumerTagChangeAfterRecoveryAsync += (sender, args) =>
            {
                try
                {
                    LogConsumerTagChanged(args.TagBefore, args.TagAfter);
                }
                catch
                {
                    _ = Interlocked.Increment(ref _swallowedEventHandlerErrors);
                }
                return Task.CompletedTask;
            };
            LogEventHandlersAttached();
        }

        #endregion

    }
}
