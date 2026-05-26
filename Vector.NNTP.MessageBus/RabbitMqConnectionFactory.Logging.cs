// RabbitMqConnectionFactory.Logging.cs -- [LoggerMessage] source-generated partial methods for all structured log messages
// emitted by the RabbitMqConnectionFactory partial class.
//
// Centralises every log call into source-generated partial methods so that:
//   1. Message templates are defined in a single file for discoverability and grep-ability.
//   2. Callers in the other partial files express intent (e.g., LogConnected) without embedding template strings.
//   3. Compile-time validation catches template/parameter mismatches at build time.
//   4. Zero-allocation logging -- the source generator emits direct calls to the logger pipeline.
//
// Uses ILogger<RabbitMqConnectionFactory> received via primary constructor injection, consistent with the [LoggerMessage]
// source-generator pattern mandated by CONTRIBUTING.md.  The primary constructor's `logger` parameter is exposed
// via a `private ILogger Logger { get; }` property in RabbitMqConnectionFactory.cs, which satisfies the source generator's
// requirement for a field or property named `logger` or `Logger` of type ILogger on the containing type.
//
// Performance:
//   Source-generated methods avoid per-call string formatting, value-type boxing, and params object[] allocation.
//   The generated code includes an IsEnabled guard that skips message formatting entirely when the target log level
//   is disabled -- eliminating the need for manual logger.IsEnabled() checks at call sites.
//
// Thread safety:
//   The source-generated methods access only the Logger property (backed by the primary constructor's `logger`
//   parameter).  ILogger is thread-safe by contract.  Safe for concurrent invocation from any thread without
//   synchronisation.
//
// Callers (by partial file):
//   RabbitMqConnectionFactory.cs               -- LogConnecting, LogTlsEnabled, LogClientProperty
//   RabbitMqConnectionFactory.Connection.cs    -- LogConnected, LogConnectionCancelled, LogConnectionFailed
//   RabbitMqConnectionFactory.EventHandlers.cs -- LogShutdownApplication, LogShutdownBroker, LogCallbackException,
//                                         LogConnectionBlocked, LogConnectionUnblocked, LogRecoverySucceeded,
//                                         LogRecoveryFailed, LogRecoveryFatal, LogConsumerTagChanged,
//                                         LogEventHandlersAttached
//
// Event ID allocation:
//   100  Connecting                       -- Connection attempt parameters (Information)
//   101  Connected                        -- Connection established with negotiated parameters (Information)
//   102  ConnectionCancelled              -- Connection attempt cancelled (Information)
//   103  ConnectionFailed                 -- All endpoints unreachable (Error)
//   104  TlsEnabled                       -- TLS configured with per-host SNI (Information)
//   105  ClientProperty                   -- Single client property key-value pair (Debug)
//   110  ShutdownApplication              -- Application-initiated shutdown (Information)
//   111  ShutdownBroker                   -- Broker-initiated shutdown (Warning)
//   112  CallbackException                -- Unhandled callback exception (Error)
//   113  ConnectionBlocked                -- Broker flow-control activated (Warning)
//   114  ConnectionUnblocked              -- Broker flow-control lifted (Information)
//   115  RecoverySucceeded                -- Automatic recovery succeeded (Information)
//   116  RecoveryFailed                   -- Automatic recovery failed (Warning)
//   117  ConsumerTagChanged               -- Consumer tag reassigned after recovery (Information)
//   118  EventHandlersAttached            -- All lifecycle event handlers subscribed (Debug)
//   119  RecoveryFatal                    -- Consecutive recovery threshold reached, shutting down (Critical)
//
// Log level policy (aligned with CONTRIBUTING.md Log Levels):
//   Information  -- Connection attempt, connected, cancelled, TLS enabled, application shutdown, unblocked,
//                   recovery succeeded, consumer tag changed.
//   Warning      -- Broker-initiated shutdown, connection blocked, recovery failed.
//   Error        -- Connection failed, callback exception.
//   Critical     -- Recovery fatal -- consecutive threshold reached, initiating shutdown.
//   Debug        -- Client properties, event handlers attached.
//
// ASCII compliance (CONTRIBUTING.md ASCII-Only Log Messages):
//   All Message strings use only ASCII characters (U+0020-U+007E).  Em-dashes are replaced with "--".
//
// Security:
//   No method in this file logs Username, Password, or any other credential.  LogConnecting logs only transport
//   parameters (endpoints, vhost, SSL, heartbeat, recovery interval, socket timeout, frame max).  LogClientProperty
//   iterates factory.ClientProperties -- a separate dictionary that does not contain credentials.
//   FormatClientPropertyValue decodes byte[] as UTF-8 with a length cap -- the library's default properties
//   (product, version, copyright, information) are safe operational metadata.
//
// Cross-platform:
//   Fully portable.  All methods are partial declarations with [LoggerMessage] attributes -- the source generator
//   emits platform-independent C# code.  No P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.
//   Compatible with Windows (x64) and Linux (x64) on .NET 8.
//
// SIMD applicability:
//   Not applicable.  This file contains only partial method declarations for the [LoggerMessage] source generator.
//   There is no computational logic, no contiguous memory buffers, and no vectorisable operations.

namespace Vector.NNTP.MessageBus
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMqConnectionFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID ranges:</b> 100-105 for connection lifecycle events; 110-119 for connection event
    /// handler events.</para>
    ///
    /// <para><b>Pattern:</b> Each method is a <see langword="private"/> <see langword="partial"/> method annotated
    /// with <see cref="LoggerMessageAttribute"/>.  The source generator emits the implementation at compile time,
    /// providing zero-allocation logging when the log level is disabled and compile-time validation of message
    /// templates.  The generator discovers the <see cref="ILogger"/> instance via the <c>Logger</c> property
    /// declared in <c>RabbitMqConnectionFactory.cs</c>.</para>
    ///
    /// <para><b>Security:</b> No method in this file logs <see cref="Configuration.RabbitMQOptions.Username"/>,
    /// <see cref="Configuration.RabbitMQOptions.Password"/>, or any other credential.  Only transport parameters,
    /// AMQP negotiated values, and operational metadata are emitted.</para>
    ///
    /// <para><b>ASCII compliance:</b> All <c>Message</c> strings contain only ASCII characters (U+0020-U+007E)
    /// per CONTRIBUTING.md.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  Source-generated logging uses BCL
    /// <c>Microsoft.Extensions.Logging</c> APIs available on all .NET 8 runtimes (Windows x64, Linux x64).
    /// No P/Invoke, no OS-specific APIs.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  Log method stubs are compile-time generated; no runtime
    /// data processing occurs in these declarations.</para>
    /// </remarks>
    public sealed partial class RabbitMqConnectionFactory
    {

        #region Logging -- Connection Lifecycle (100-105)

        /// <summary>
        /// Logs the connection attempt parameters at <see cref="LogLevel.Information"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="CreateConnectionAsync"/> -- immediately before factory and endpoint
        /// construction.</para>
        ///
        /// <para><b>Security:</b> Logs only transport parameters.
        /// <see cref="Configuration.RabbitMQOptions.Username"/> and
        /// <see cref="Configuration.RabbitMQOptions.Password"/> are intentionally excluded -- they are set on the
        /// <see cref="RabbitMQ.Client.ConnectionFactory"/> but never appear in any log output.</para>
        /// </remarks>
        [LoggerMessage(EventId = 100, Level = LogLevel.Information,
            Message = "RabbitMQ connecting -- Endpoints=[{Endpoints}], VHost={VirtualHost}, SSL={EnableSsl}, " +
                      "Heartbeat={Heartbeat}s, RecoveryInterval={Recovery}s, SocketTimeout={SocketTimeout}s, FrameMax={FrameMax}")]
        private partial void LogConnecting(string endpoints, string virtualHost, bool enableSsl,
            int heartbeat, int recovery, int socketTimeout, uint frameMax);

        /// <summary>
        /// Logs a successful connection with negotiated parameters at <see cref="LogLevel.Information"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ConnectWithLoggingAsync"/> -- success path after
        /// <see cref="RabbitMQ.Client.ConnectionFactory.CreateConnectionAsync(IEnumerable{AmqpTcpEndpoint}, CancellationToken)"/>
        /// completes.</para>
        ///
        /// <para>The endpoint identifies which specific broker node accepted the connection from the multi-host endpoint
        /// list -- essential for verifying failover behaviour and correlating with the RabbitMQ Management UI's connection
        /// list.</para>
        ///
        /// <para><b>Negotiated vs. requested:</b> The frame max, channel max, and heartbeat values logged here are the
        /// <em>negotiated</em> values agreed upon during the AMQP handshake -- they may differ from the requested values
        /// in <see cref="Configuration.RabbitMQOptions"/> if the broker imposes lower limits.</para>
        /// </remarks>
        [LoggerMessage(EventId = 101, Level = LogLevel.Information,
            Message = "RabbitMQ connected -- Endpoint={Endpoint}, FrameMax={FrameMax}, ChannelMax={ChannelMax}, " +
                      "Heartbeat={Heartbeat}s, Elapsed={ElapsedMs:F1}ms")]
        private partial void LogConnected(string endpoint, uint frameMax, ushort channelMax, ushort heartbeat, double elapsedMs);

        /// <summary>
        /// Logs a cancelled connection attempt at <see cref="LogLevel.Information"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ConnectWithLoggingAsync"/> -- cancellation path.  Cancellation during host
        /// shutdown is expected behaviour, not an error.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> rather than <see cref="LogLevel.Warning"/>
        /// because cancellation is an expected outcome during host shutdown -- the <see cref="ConnectionPool"/>
        /// propagates the host's stopping token to the connection attempt.</para>
        /// </remarks>
        [LoggerMessage(EventId = 102, Level = LogLevel.Information,
            Message = "RabbitMQ connection attempt cancelled -- Endpoints=[{Endpoints}], Elapsed={ElapsedMs:F1}ms")]
        private partial void LogConnectionCancelled(string endpoints, double elapsedMs);

        /// <summary>
        /// Logs a failed connection attempt at <see cref="LogLevel.Error"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ConnectWithLoggingAsync"/> -- failure path when all endpoints are
        /// unreachable.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Error"/> because all configured broker endpoints failed --
        /// the connection attempt is exhausted.  The caller (<see cref="ConnectionPool.StartingAsync"/>) will retry
        /// with exponential back-off; this log entry provides the per-attempt failure detail.</para>
        /// </remarks>
        [LoggerMessage(EventId = 103, Level = LogLevel.Error,
            Message = "RabbitMQ connection failed -- Endpoints=[{Endpoints}], Elapsed={ElapsedMs:F1}ms. All configured brokers were unreachable")]
        private partial void LogConnectionFailed(Exception ex, string endpoints, double elapsedMs);

        /// <summary>
        /// Logs that TLS is enabled with per-host SNI at <see cref="LogLevel.Information"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="BuildEndpoints"/> -- when <see cref="Configuration.RabbitMQOptions.EnableSsl"/>
        /// is <c>true</c>.</para>
        ///
        /// <para>Logs the allowed TLS protocol versions (<see cref="AllowedTlsProtocols"/>) to confirm that only
        /// TLS 1.2 and TLS 1.3 are permitted -- older protocols (SSL 3.0, TLS 1.0, TLS 1.1) are rejected.</para>
        /// </remarks>
        [LoggerMessage(EventId = 104, Level = LogLevel.Information,
            Message = "RabbitMQ TLS enabled -- per-host SNI configured for {HostCount} endpoint(s), protocols={AllowedProtocols}")]
        private partial void LogTlsEnabled(int hostCount, string allowedProtocols);

        /// <summary>
        /// Logs a single client property key-value pair at <see cref="LogLevel.Debug"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="LogClientProperties"/> -- iterates all
        /// <see cref="RabbitMQ.Client.ConnectionFactory.ClientProperties"/> entries.</para>
        ///
        /// <para><b>Guard:</b> The caller checks <see cref="ILogger.IsEnabled(LogLevel)"/> before iterating, so this
        /// method is only called when Debug logging is active -- per CONTRIBUTING.md Guard Clauses for Expensive
        /// Logging.</para>
        ///
        /// <para><b>Security:</b> Client properties are a separate dictionary from credentials.  No credentials are
        /// present.</para>
        /// </remarks>
        [LoggerMessage(EventId = 105, Level = LogLevel.Debug,
            Message = "RabbitMQ ClientProperty: {Key}={Value}")]
        private partial void LogClientProperty(string key, string value);

        #endregion

        #region Logging -- Connection Event Handlers (110-119)

        /// <summary>
        /// Logs an application-initiated connection shutdown at <see cref="LogLevel.Information"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.ConnectionShutdownAsync"/> handler when
        /// <see cref="Client.ShutdownEventArgs.Initiator"/> is
        /// <see cref="RabbitMQ.Client.ShutdownInitiator.Application"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> because application-initiated shutdowns are
        /// expected (host stopping, graceful drain).  The AMQP reply code and text provide the shutdown reason for
        /// operational correlation.</para>
        /// </remarks>
        [LoggerMessage(EventId = 110, Level = LogLevel.Information,
            Message = "RabbitMQ connection shutdown (application-initiated) -- ReplyCode={ReplyCode}, ReplyText={ReplyText}")]
        private partial void LogShutdownApplication(ushort replyCode, string replyText);

        /// <summary>
        /// Logs a broker-initiated connection shutdown at <see cref="LogLevel.Warning"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.ConnectionShutdownAsync"/> handler when
        /// <see cref="Client.ShutdownEventArgs.Initiator"/> is not
        /// <see cref="RabbitMQ.Client.ShutdownInitiator.Application"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because broker-initiated shutdowns indicate an
        /// external event requiring operator awareness (maintenance drain, resource limit, node failure).  Automatic
        /// recovery will attempt reconnection -- the recovery interval is logged for context.</para>
        /// </remarks>
        [LoggerMessage(EventId = 111, Level = LogLevel.Warning,
            Message = "RabbitMQ connection shutdown (broker-initiated) -- Initiator={Initiator}, ReplyCode={ReplyCode}, " +
                      "ReplyText={ReplyText}. Automatic recovery will attempt reconnection in {RecoveryInterval}s")]
        private partial void LogShutdownBroker(Exception? ex, string initiator, ushort replyCode, string replyText, int recoveryInterval);

        /// <summary>
        /// Logs an unhandled callback exception at <see cref="LogLevel.Error"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.CallbackExceptionAsync"/> handler.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Error"/> per CONTRIBUTING.md Log Levels ("callback
        /// exceptions").  An unhandled exception in a consumer callback indicates a bug in application code -- the
        /// detail string provides the callback context for diagnosis.</para>
        /// </remarks>
        [LoggerMessage(EventId = 112, Level = LogLevel.Error,
            Message = "RabbitMQ callback exception -- an application event handler threw an unhandled exception. Detail={Detail}")]
        private partial void LogCallbackException(Exception ex, string detail);

        /// <summary>
        /// Logs that the broker has blocked the connection (flow control) at <see cref="LogLevel.Warning"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.ConnectionBlockedAsync"/> handler.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because broker blocks are operator-actionable
        /// (add memory, free disk, reduce publish rate) and require attention.  The reason is the first indicator of
        /// broker pressure -- common values are <c>"low on memory"</c>, <c>"low on disk"</c>, and
        /// <c>"resource limit exceeded"</c>.</para>
        ///
        /// <para>The corresponding <see cref="LogConnectionUnblocked"/> provides the resolution signal -- operators can
        /// correlate block/unblock pairs to measure flow-control duration.</para>
        /// </remarks>
        [LoggerMessage(EventId = 113, Level = LogLevel.Warning,
            Message = "RabbitMQ connection BLOCKED by broker -- Reason={Reason}. All publishes on this connection are paused until the broker lifts flow control")]
        private partial void LogConnectionBlocked(string reason);

        /// <summary>
        /// Logs that broker flow control has been lifted at <see cref="LogLevel.Information"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.ConnectionUnblockedAsync"/> handler.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> because the block condition has resolved --
        /// this is the "all clear" signal paired with <see cref="LogConnectionBlocked"/>.  Operators can correlate the
        /// pair to measure flow-control duration in structured log sinks.</para>
        /// </remarks>
        [LoggerMessage(EventId = 114, Level = LogLevel.Information,
            Message = "RabbitMQ connection UNBLOCKED -- broker flow control lifted. Publishes have resumed")]
        private partial void LogConnectionUnblocked();

        /// <summary>
        /// Logs a successful automatic recovery at <see cref="LogLevel.Information"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.RecoverySucceededAsync"/> handler.</para>
        ///
        /// <para>The connection name is the <see cref="RabbitMQ.Client.IConnection.ClientProvidedName"/> set by
        /// <see cref="CreateFactory"/> via <see cref="ApplicationName"/>.  In multi-service deployments sharing the same
        /// broker cluster, this value identifies which service instance recovered.</para>
        ///
        /// <para>The endpoint identifies which broker node the connection recovered to -- this may differ from the
        /// original node if the client library failed over to a different endpoint in the configured list.</para>
        /// </remarks>
        [LoggerMessage(EventId = 115, Level = LogLevel.Information,
            Message = "RabbitMQ connection recovery SUCCEEDED -- Endpoint={Endpoint}, ConnectionName={ConnectionName}. " +
                      "Connection and topology restored; all channels and consumers are active")]
        private partial void LogRecoverySucceeded(string endpoint, string connectionName);

        /// <summary>
        /// Logs a failed automatic recovery attempt at <see cref="LogLevel.Warning"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.ConnectionRecoveryErrorAsync"/> handler.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> rather than <see cref="LogLevel.Error"/> because
        /// automatic recovery will continue retrying at the configured interval.  A single failed recovery attempt is a
        /// transient condition -- persistent failure will generate repeated warnings, which alerting systems can escalate
        /// based on frequency.  If the consecutive failure count reaches
        /// <see cref="Configuration.RabbitMQOptions.MaxConsecutiveRecoveryFailures"/>, the fail-fast mechanism escalates to
        /// <see cref="LogLevel.Critical"/> via <see cref="LogRecoveryFatal"/>.</para>
        /// </remarks>
        [LoggerMessage(EventId = 116, Level = LogLevel.Warning,
            Message = "RabbitMQ connection recovery FAILED (attempt {ConsecutiveFailures}) -- will retry in {RecoveryInterval}s. " +
                      "Ensure at least one broker endpoint is reachable")]
        private partial void LogRecoveryFailed(Exception ex, int recoveryInterval, int consecutiveFailures);

        /// <summary>
        /// Logs a consumer tag reassignment after topology recovery at <see cref="LogLevel.Information"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.ConsumerTagChangeAfterRecoveryAsync"/> handler.</para>
        ///
        /// <para>Consumer tags are server-assigned identifiers for active consumers.  After topology recovery, the broker
        /// may assign new tags that differ from the pre-recovery tags.  The RabbitMQ client library updates its internal
        /// consumer registry automatically; this log entry provides visibility for debugging message-routing issues after
        /// recovery.</para>
        /// </remarks>
        [LoggerMessage(EventId = 117, Level = LogLevel.Information,
            Message = "RabbitMQ consumer tag changed after recovery -- OldTag={OldTag}, NewTag={NewTag}")]
        private partial void LogConsumerTagChanged(string oldTag, string newTag);

        /// <summary>
        /// Logs that all connection lifecycle event handlers have been attached at <see cref="LogLevel.Debug"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> -- final statement after all event
        /// subscriptions.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Debug"/> because this is a diagnostic confirmation that the
        /// setup sequence completed -- not an operator-visible milestone.  The subsequent <see cref="LogConnected"/> call
        /// (at <see cref="LogLevel.Information"/>) is the operator-facing signal that the connection is ready.</para>
        /// </remarks>
        [LoggerMessage(EventId = 118, Level = LogLevel.Debug,
            Message = "RabbitMQ connection lifecycle event handlers attached")]
        private partial void LogEventHandlersAttached();

        /// <summary>
        /// Logs that the consecutive recovery failure threshold has been reached and the application is initiating a
        /// graceful shutdown at <see cref="LogLevel.Critical"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AttachConnectionEventHandlers"/> --
        /// <see cref="RabbitMQ.Client.IConnection.ConnectionRecoveryErrorAsync"/> handler, when
        /// <see cref="_consecutiveRecoveryFailures"/> reaches
        /// <see cref="Configuration.RabbitMQOptions.MaxConsecutiveRecoveryFailures"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Critical"/> because the application is about to terminate
        /// itself.  This is the highest severity -- the connection is in a permanently failed state that the RabbitMQ client
        /// library's automatic recovery cannot resolve.  The process must be restarted by the external supervisor (systemd,
        /// container orchestrator) to establish a fresh connection.</para>
        ///
        /// <para><b>Exactly once:</b> The caller uses an <c>== threshold</c> comparison (not <c>>=</c>) so this log
        /// message is emitted exactly once per unrecoverable failure episode.  Subsequent recovery attempts after the
        /// <see cref="IHostApplicationLifetime.StopApplication"/> call may still fire the Warning-level
        /// <see cref="LogRecoveryFailed"/> during the shutdown grace period, but the Critical escalation occurs only at
        /// the threshold crossing.</para>
        /// </remarks>
        [LoggerMessage(EventId = 119, Level = LogLevel.Critical,
            Message = "RabbitMQ connection recovery has FAILED {Threshold} consecutive times (interval={RecoveryInterval}s) -- " +
                      "connection is presumed unrecoverable. Initiating application shutdown for external supervisor restart")]
        private partial void LogRecoveryFatal(int threshold, int recoveryInterval);

        #endregion

    }
}
