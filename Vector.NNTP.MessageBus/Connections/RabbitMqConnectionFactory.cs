// RabbitMqConnectionFactory.cs -- creates and configures the shared RabbitMQ IConnection from RabbitMQOptions.
//
// Called by ConnectionPool.StartingAsync during the host's "starting" lifecycle phase.  The returned IConnection
// is stored in ConnectionPool and multiplexed across all NntpWorker instances (each creates its own IChannel).
//
// Responsibility summary:
//   CreateConnectionAsync         -- build factory, try endpoints, log outcome
//   CreateFactory                 -- configure AMQP params, client props, TLS
//   PopulateClientProperties      -- diagnostic metadata for the RabbitMQ Management UI
//   BuildEndpoints                -- map Hosts[] -> AmqpTcpEndpoint list with per-host SNI
//
// Partial class files (split by responsibility):
//   RabbitMqConnectionFactory.cs              -- constants, public API, factory configuration (this file)
//   RabbitMqConnectionFactory.Connection.cs   -- async connect with structured logging
//   RabbitMqConnectionFactory.Diagnostics.cs  -- client property formatting
//   RabbitMqConnectionFactory.EventHandlers.cs -- shutdown, blocked, recovery, tag change
//   RabbitMqConnectionFactory.Logging.cs      -- [LoggerMessage] source-generated partial methods
//
// Event ID allocation (RabbitMqConnectionFactory.Logging.cs):
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
// Caller:
//   ConnectionPool.StartingAsync -- retry loop with exponential back-off (2s base, 30s cap, 1s jitter).
//
// Security:
//   Credentials (Username, Password) are set on the RabbitMQ.Client.ConnectionFactory for SASL PLAIN authentication
//   but are never logged.  LogConnecting logs only transport parameters; LogClientProperties iterates
//   factory.ClientProperties which does not include Password or Username.  The factory object is method-scoped
//   and not stored in any long-lived field.
//
// Cross-platform:
//   Fully portable.  All APIs used (Assembly.GetEntryAssembly, Environment.MachineName, Environment.ProcessId,
//   Environment.OSVersion, RuntimeInformation.FrameworkDescription, Stopwatch, SslOption, AmqpTcpEndpoint,
//   FormattingUtilities.FormatEndpointSummary) are part of the .NET Base Class Library and behave identically on
//   Windows (x64) and Linux (x64) on .NET 8.  No P/Invoke, no OS-specific APIs, no architecture-specific
//   intrinsics.
//
// SIMD applicability:
//   Not applicable.  All methods perform object construction, dictionary population, and string formatting.
//   There are no contiguous memory buffers, byte-level pattern searches, or bulk numeric operations that
//   would benefit from vector instructions.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Vector.NNTP.MessageBus.Configuration;
using RabbitMQ.Client;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Factory that creates a shared <see cref="IConnection"/> from <see cref="RabbitMQOptions"/>.  The returned
    /// connection is stored in <see cref="ConnectionPool"/> (a DI singleton) and multiplexed across all
    /// <c>NntpWorker</c> instances -- each worker creates its own <see cref="IChannel"/> from the shared connection.
    /// </summary>
    /// <remarks>
    /// <para><b>DI registration:</b> Registered as a DI singleton in <c>host Program.*.cs</c> via
    /// <c>builder.Services.AddSingleton&lt;RabbitMqConnectionFactory&gt;()</c>.  Injected into <see cref="ConnectionPool"/>
    /// which calls <see cref="CreateConnectionAsync"/> during the host's "starting" lifecycle phase.</para>
    ///
    /// <para><b>Multi-host failover:</b> The <see cref="RabbitMQOptions.Hosts"/> array may contain one or more broker
    /// addresses (e.g., an AWS Amazon MQ HA cluster providing 3 nodes).  Each host is mapped to an
    /// <see cref="AmqpTcpEndpoint"/> and passed to
    /// <see cref="ConnectionFactory.CreateConnectionAsync(IEnumerable{AmqpTcpEndpoint}, CancellationToken)"/>.
    /// The client library attempts each endpoint in order until one succeeds; automatic recovery cycles through all
    /// endpoints on reconnect, providing transparent failover.</para>
    ///
    /// <para><b>Automatic recovery:</b> Both connection and topology recovery are unconditionally enabled -- they are not
    /// operator-configurable because the <see cref="ConnectionPool"/> lifecycle model
    /// (<c>Empty -> Connected -> Cleared</c>) requires automatic recovery to function.  Disabling recovery would cause
    /// transient broker failures to permanently break the application until a process restart.</para>
    ///
    /// <para><b>Connection lifecycle events:</b> After a successful connect, <see cref="AttachConnectionEventHandlers"/>
    /// subscribes to all <see cref="IConnection"/> lifecycle events:</para>
    /// <list type="bullet">
    ///   <item><description><see cref="IConnection.ConnectionShutdownAsync"/> -- application and broker-initiated
    ///     disconnects.</description></item>
    ///   <item><description><see cref="IConnection.CallbackExceptionAsync"/> -- unhandled exceptions in consumer
    ///     callbacks.</description></item>
    ///   <item><description><see cref="IConnection.ConnectionBlockedAsync"/> /
    ///     <see cref="IConnection.ConnectionUnblockedAsync"/> -- broker flow-control (memory/disk
    ///     alarms).</description></item>
    ///   <item><description><see cref="IConnection.RecoverySucceededAsync"/> /
    ///     <see cref="IConnection.ConnectionRecoveryErrorAsync"/> -- automatic recovery outcomes.</description></item>
    ///   <item><description><see cref="IConnection.ConsumerTagChangeAfterRecoveryAsync"/> -- consumer tag reassignment
    ///     after topology recovery.</description></item>
    /// </list>
    ///
    /// <para>These fire for the lifetime of the singleton connection and provide full observability of broker-initiated
    /// state changes (failover, flow-control, maintenance drains) without polling.</para>
    ///
    /// <para><b>Connection parameters:</b></para>
    /// <list type="bullet">
    ///   <item><description><c>AutomaticRecoveryEnabled</c> + <c>TopologyRecoveryEnabled</c> -- always <c>true</c>;
    ///     non-configurable (see above).</description></item>
    ///   <item><description><c>NetworkRecoveryInterval</c> -- from
    ///     <see cref="RabbitMQOptions.NetworkRecoveryIntervalSeconds"/>; delay between reconnection
    ///     attempts.</description></item>
    ///   <item><description><c>RequestedHeartbeat</c> -- from <see cref="RabbitMQOptions.RequestedHeartbeatSeconds"/>;
    ///     AMQP heartbeat interval; the server declares the connection dead after 2x this value.</description></item>
    ///   <item><description><c>RequestedFrameMax</c> (<see cref="MaxFrameSize"/>) -- maximum AMQP frame size; keeps
    ///     per-frame memory bounded while the client library splits large article payloads across multiple
    ///     frames.</description></item>
    ///   <item><description><c>SocketReadTimeout</c> / <c>SocketWriteTimeout</c> -- from
    ///     <see cref="RabbitMQOptions.SocketTimeoutSeconds"/>; prevents indefinite hangs on silently-dropped TCP
    ///     connections.</description></item>
    /// </list>
    ///
    /// <para><b>Client properties</b> are populated with application name, version, platform, runtime, machine name, and
    /// PID -- visible in the RabbitMQ Management UI under the connection details tab.  Credentials are never included in
    /// client properties or log output.</para>
    ///
    /// <para><b>Logging:</b> Uses <c>[LoggerMessage]</c> source-generated partial methods with
    /// <see cref="ILogger{TCategoryName}"/> received via the primary constructor.  All log methods are defined in
    /// <c>RabbitMqConnectionFactory.Logging.cs</c> for discoverability and consistency.  This is consistent with the project-wide
    /// <c>[LoggerMessage]</c> pattern defined in CONTRIBUTING.md.</para>
    ///
    /// <para><b>Security:</b> <see cref="RabbitMQOptions.Username"/> and <see cref="RabbitMQOptions.Password"/> are set on
    /// the <see cref="ConnectionFactory"/> for SASL PLAIN authentication but are never logged.
    /// <see cref="LogConnecting"/> logs only transport parameters (endpoints, vhost, SSL, heartbeat, recovery interval,
    /// socket timeout, frame max).  <see cref="LogClientProperties"/> iterates
    /// <see cref="ConnectionFactory.ClientProperties"/> which is a separate dictionary that does not
    /// contain credentials.</para>
    ///
    /// <para><b>Thread safety:</b> All methods read only from <c>static readonly</c> fields, the <c>options</c> parameter,
    /// and the injected logger.  The class is safe to call concurrently, though in practice it is called exactly once per
    /// application lifetime by <see cref="ConnectionPool.StartingAsync"/>.</para>
    ///
    /// <para><b>Partial class files:</b></para>
    /// <list type="bullet">
    ///   <item><description><c>RabbitMqConnectionFactory.cs</c> -- constants, public API entry point, factory configuration (this
    ///     file).</description></item>
    ///   <item><description><c>RabbitMqConnectionFactory.Connection.cs</c> -- async connection attempt with structured success,
    ///     cancellation, and failure logging.</description></item>
    ///   <item><description><c>RabbitMqConnectionFactory.Diagnostics.cs</c> -- client property value formatting.</description></item>
    ///   <item><description><c>RabbitMqConnectionFactory.EventHandlers.cs</c> -- <see cref="IConnection"/> lifecycle event handler
    ///     subscriptions (shutdown, blocked, recovery, consumer tag change).</description></item>
    ///   <item><description><c>RabbitMqConnectionFactory.Logging.cs</c> -- all <c>[LoggerMessage]</c> source-generated partial
    ///     methods consumed by the other partial files.</description></item>
    /// </list>
    ///
    /// <para><b>Primary constructor parameters:</b></para>
    /// <list type="bullet">
    ///   <item><description><c>logger</c> -- <see cref="ILogger{TCategoryName}"/> scoped to
    ///     <see cref="RabbitMqConnectionFactory"/> for structured logging via <c>[LoggerMessage]</c> source
    ///     generation.</description></item>
    ///   <item><description><c>hostLifetime</c> -- <see cref="IHostApplicationLifetime"/> used by the
    ///     <see cref="IConnection.ConnectionRecoveryErrorAsync"/> event handler to trigger a graceful application shutdown
    ///     when the consecutive recovery failure count reaches
    ///     <see cref="RabbitMQOptions.MaxConsecutiveRecoveryFailures"/>.  This enables the external process supervisor
    ///     (systemd, container orchestrator) to restart the process with a fresh connection.</description></item>
    /// </list>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  All APIs used are BCL types available on all .NET 8 runtimes
    /// (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  All methods perform object construction, dictionary population,
    /// and string formatting.  No vectorisable computation paths.</para>
    /// </remarks>
    public sealed partial class RabbitMqConnectionFactory(ILogger<RabbitMqConnectionFactory> logger, IHostApplicationLifetime hostLifetime)
    {

        #region Constants

        /// <summary>
        /// Maximum AMQP frame size in bytes (128 KB).  Keeps per-frame memory bounded while the client library
        /// transparently splits large article payloads across multiple frames.
        /// </summary>
        /// <remarks>
        /// <para>The default RabbitMQ broker frame max is 131,072 bytes.  Matching the broker's default avoids a protocol
        /// negotiation round-trip where the broker would clamp a larger request.  The value is large enough for efficient
        /// throughput (fewer frames per article) while small enough to limit per-channel memory reservation.</para>
        ///
        /// <para><b>Not operator-configurable:</b> Unlike heartbeat, recovery interval, and socket timeout -- which have
        /// legitimate environment-specific tuning scenarios -- the frame max must match the broker's configured
        /// <c>frame_max</c> to avoid silent truncation or negotiation failures.  The default 131,072 matches the
        /// out-of-the-box broker configuration for RabbitMQ and Amazon MQ.  If a non-default broker <c>frame_max</c> is
        /// required, it should be changed here and redeployed -- not exposed as a runtime knob that can silently break
        /// protocol negotiation.</para>
        /// </remarks>
        private const uint MaxFrameSize = 131_072;

        /// <summary>
        /// TLS protocol versions permitted for AMQPS connections.  Only TLS 1.2 and TLS 1.3 are enabled; older protocols
        /// (SSL 3.0, TLS 1.0, TLS 1.1) are rejected.
        /// </summary>
        /// <remarks>
        /// <para>Applied per-endpoint in <see cref="BuildEndpoints"/> via <see cref="SslOption.Version"/>.  Declared as a
        /// constant to avoid recreating the bitwise-OR value on every endpoint construction.</para>
        /// </remarks>
        private const System.Security.Authentication.SslProtocols AllowedTlsProtocols =
            System.Security.Authentication.SslProtocols.Tls12 |
            System.Security.Authentication.SslProtocols.Tls13;

        /// <summary>
        /// Maximum byte length of an AMQP client property value that will be decoded for log output.  Values exceeding
        /// this length are truncated to prevent a malicious or misconfigured broker from causing excessive memory
        /// allocation during diagnostic logging.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> 1,024 bytes.  The largest standard RabbitMQ client property (<c>copyright</c>) is
        /// approximately 60 bytes.  1 KB provides ample headroom for non-standard properties while capping memory
        /// consumption per property at a known bound.</para>
        /// </remarks>
        private const int MaxClientPropertyValueLength = 1_024;

        #endregion

        #region Fields

        /// <summary>
        /// Tracks the number of consecutive automatic recovery failures since the last successful recovery or initial
        /// connection.  Incremented atomically in the <see cref="IConnection.ConnectionRecoveryErrorAsync"/> handler;
        /// reset to zero in the <see cref="IConnection.RecoverySucceededAsync"/> handler.
        /// </summary>
        /// <remarks>
        /// <para><b>Thread safety:</b> Accessed exclusively via <see cref="Interlocked.Increment(ref int)"/> (in the
        /// failure handler) and <see cref="Volatile.Write(ref int, int)"/> (in the success handler).  Both handlers
        /// execute on the RabbitMQ client library's internal I/O thread, which is single-threaded per connection, so
        /// the interlocked operations are a defensive measure rather than a strict necessity.</para>
        ///
        /// <para><b>Lifecycle:</b> This field is meaningful only for the lifetime of the single connection created by
        /// <see cref="ConnectionPool.StartingAsync"/>.  The <see cref="RabbitMqConnectionFactory"/> singleton is never
        /// reused for a second connection within the same process.</para>
        /// </remarks>
        private int _consecutiveRecoveryFailures;

        /// <summary>
        /// Counts exceptions swallowed by the <c>catch</c> blocks in <see cref="AttachConnectionEventHandlers"/> to provide
        /// last-resort observability when the logging pipeline itself is broken.
        /// </summary>
        /// <remarks>
        /// <para><b>Rationale:</b> The event handler catch blocks must swallow exceptions to protect the RabbitMQ client
        /// library's internal I/O thread.  However, silently swallowing all exceptions creates a diagnostic black hole --
        /// if the logger fails (sink exception, <see cref="ObjectDisposedException"/> during shutdown, OOM in
        /// <see cref="StringBuilder"/>), no trace of the failure exists anywhere.  This counter provides a minimal
        /// signal that can be inspected via health-check endpoints, memory dumps, or a debugger without risking the
        /// recursive failure that a log-the-failure approach would introduce.</para>
        ///
        /// <para><b>Thread safety:</b> Incremented via <see cref="Interlocked.Increment(ref int)"/>.  Although the
        /// handlers execute on the client library's single I/O thread, the counter may be read from other threads
        /// (health checks, diagnostics), so atomic access is required for correctness.</para>
        ///
        /// <para><b>Not logged:</b> Intentionally not logged at the point of increment -- the logger is the component
        /// that just failed.  Attempting to log would risk the same exception recurring or infinite recursion if the
        /// sink is permanently broken.</para>
        /// </remarks>
        private int _swallowedEventHandlerErrors;

        #endregion

        #region Properties

        /// <summary>
        /// Exposes the primary constructor's <c>logger</c> parameter for the <c>[LoggerMessage]</c> source generator.
        /// </summary>
        /// <remarks>
        /// <para>The <c>[LoggerMessage]</c> source generator in .NET 8 requires a field or property named <c>logger</c>
        /// or <c>Logger</c> of type <see cref="ILogger"/> on the containing type.  Primary constructor parameters are
        /// captured by the compiler as unspeakable backing fields that the source generator cannot discover.  This property
        /// bridges the gap by exposing the primary constructor parameter under a name the source generator
        /// recognises.</para>
        /// </remarks>
        private ILogger Logger { get; } = logger;

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates and opens an AMQP connection to the configured RabbitMQ cluster.  All hosts in
        /// <see cref="RabbitMQOptions.Hosts"/> are tried in order; the first successful connection is returned.
        /// </summary>
        /// <remarks>
        /// <para><b>Flow:</b></para>
        /// <list type="number">
        ///   <item><description>Build a pre-formatted endpoint summary string via
        ///     <see cref="FormattingUtilities.FormatEndpointSummary"/> for use in log messages.</description></item>
        ///   <item><description>Log the connection attempt parameters (endpoints, vhost, SSL, heartbeat, recovery interval,
        ///     socket timeout, frame max) at <see cref="LogLevel.Information"/>.  Credentials are intentionally
        ///     excluded.</description></item>
        ///   <item><description>Construct the <see cref="ConnectionFactory"/> via
        ///     <see cref="CreateFactory"/> and the endpoint list via <see cref="BuildEndpoints"/>.</description></item>
        ///   <item><description>Log all client properties at <see cref="LogLevel.Debug"/> via
        ///     <see cref="LogClientProperties"/>.  Client properties are a separate dictionary from factory credentials --
        ///     <see cref="ConnectionFactory.Password"/> and
        ///     <see cref="ConnectionFactory.UserName"/> are never iterated.</description></item>
        ///   <item><description>Delegate to <see cref="ConnectWithLoggingAsync"/> which performs the actual connection
        ///     attempt and logs the outcome.  The <see cref="Stopwatch"/> timestamp is captured <em>after</em> factory
        ///     construction so the elapsed time reflects only the async TCP/AMQP handshake, not synchronous configuration
        ///     overhead.</description></item>
        /// </list>
        ///
        /// <para>On success, all <see cref="IConnection"/> lifecycle event handlers are attached via
        /// <see cref="AttachConnectionEventHandlers"/> before the connection is returned to the caller.  If event handler
        /// subscription fails, the open connection is disposed to prevent resource leaks (see
        /// <see cref="ConnectWithLoggingAsync"/>).</para>
        ///
        /// <para><b>TLS:</b> When <see cref="RabbitMQOptions.EnableSsl"/> is <c>true</c>, each
        /// <see cref="AmqpTcpEndpoint"/> is configured with an <see cref="SslOption"/> whose
        /// <see cref="SslOption.ServerName"/> is set to the individual host for correct SNI negotiation.</para>
        ///
        /// <para><b>Cancellation:</b> The <paramref name="cancellationToken"/> is forwarded to
        /// <see cref="ConnectionFactory.CreateConnectionAsync(IEnumerable{AmqpTcpEndpoint}, CancellationToken)"/>,
        /// allowing the caller (typically <see cref="ConnectionPool.StartingAsync"/>) to abort a long-running DNS
        /// resolution or TCP handshake during host shutdown.</para>
        ///
        /// <para><b>Input contract:</b> <paramref name="options"/> must have passed
        /// <see cref="RabbitMQOptions.Validate"/> -- all properties are validated, hosts are normalised (trimmed,
        /// bracket-stripped), and cross-property invariants (e.g., socket timeout >= 2x heartbeat) are enforced.  No
        /// defensive re-validation is performed here.</para>
        /// </remarks>
        /// <param name="options">Strongly-typed RabbitMQ configuration bound from <c>host configuration (IOptions)</c>.  Must have
        /// passed <see cref="RabbitMQOptions.Validate"/>.</param>
        /// <param name="cancellationToken">Cancellation token propagated to the underlying connection attempt.  Cancelled
        /// when the host is shutting down or the startup timeout expires.</param>
        /// <returns>A <see cref="Task{IConnection}"/> that resolves to an open AMQP connection with automatic recovery
        /// enabled and lifecycle event handlers attached.</returns>
        /// <exception cref="RabbitMQ.Client.Exceptions.BrokerUnreachableException">All configured endpoints failed to
        /// connect.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before a
        /// connection could be established.</exception>
        public Task<IConnection> CreateConnectionAsync(RabbitMQOptions options, CancellationToken cancellationToken = default)
        {
            string endpointSummary = FormattingUtilities.FormatEndpointSummary(options.Hosts, options.Port);
            LogConnecting(endpointSummary, options.VirtualHost, options.EnableSsl,
                options.RequestedHeartbeatSeconds, options.NetworkRecoveryIntervalSeconds,
                options.SocketTimeoutSeconds, MaxFrameSize);
            ConnectionFactory factory = CreateFactory(options);
            List<AmqpTcpEndpoint> endpoints = BuildEndpoints(options);
            LogClientProperties(factory);
            long connectStart = Stopwatch.GetTimestamp();
            return ConnectWithLoggingAsync(factory, endpoints, options, endpointSummary, connectStart, cancellationToken);
        }

        #endregion

        #region Private Methods -- Factory Configuration

        /// <summary>
        /// Builds and configures the <see cref="ConnectionFactory"/> with all AMQP connection parameters,
        /// client properties, and optional TLS settings.
        /// </summary>
        /// <remarks>
        /// <para><b>Configuration applied:</b></para>
        /// <list type="bullet">
        ///   <item><description>Credentials (<see cref="RabbitMQOptions.Username"/>,
        ///     <see cref="RabbitMQOptions.Password"/>) and <see cref="RabbitMQOptions.VirtualHost"/> from the validated
        ///     options.</description></item>
        ///   <item><description>Automatic recovery + topology recovery -- always enabled (non-configurable); required by the
        ///     <see cref="ConnectionPool"/> lifecycle model.</description></item>
        ///   <item><description><see cref="RabbitMQOptions.NetworkRecoveryIntervalSeconds"/> -- delay between reconnection
        ///     attempts.</description></item>
        ///   <item><description><see cref="RabbitMQOptions.RequestedHeartbeatSeconds"/> -- AMQP heartbeat; dead after
        ///     2x.</description></item>
        ///   <item><description><see cref="MaxFrameSize"/> -- 128 KB maximum AMQP frame.</description></item>
        ///   <item><description><see cref="RabbitMQOptions.SocketTimeoutSeconds"/> -- TCP read/write timeout.  Guaranteed
        ///     >= 2x heartbeat by <see cref="RabbitMQOptions.Validate"/>.</description></item>
        ///   <item><description>Client properties -- diagnostic metadata for the RabbitMQ Management UI via
        ///     <see cref="PopulateClientProperties"/>.</description></item>
        /// </list>
        ///
        /// <para><b>TLS note:</b> SSL/TLS is configured per-endpoint in <see cref="BuildEndpoints"/> rather than on the
        /// factory, so that each <see cref="AmqpTcpEndpoint"/> carries its own <see cref="SslOption"/> with the correct
        /// per-host SNI <see cref="SslOption.ServerName"/>.</para>
        ///
        /// <para><b>Security:</b> The <see cref="ConnectionFactory.Password"/> is stored in the factory's
        /// internal field for SASL PLAIN authentication.  It is never logged -- <see cref="LogConnecting"/> excludes
        /// credentials, and <see cref="LogClientProperties"/> iterates only
        /// <see cref="ConnectionFactory.ClientProperties"/> (a separate dictionary).  The factory is
        /// method-scoped and passed directly to <see cref="ConnectWithLoggingAsync"/>; it is not stored in any long-lived
        /// field.</para>
        /// </remarks>
        /// <param name="options">Validated RabbitMQ options.</param>
        /// <returns>A fully-configured, ready-to-connect factory.  Not <see cref="IDisposable"/> -- no cleanup
        /// required.</returns>
        private static ConnectionFactory CreateFactory(RabbitMQOptions options)
        {
            TimeSpan heartbeat = TimeSpan.FromSeconds(options.RequestedHeartbeatSeconds);
            TimeSpan recoveryInterval = TimeSpan.FromSeconds(options.NetworkRecoveryIntervalSeconds);
            TimeSpan socketTimeout = TimeSpan.FromSeconds(options.SocketTimeoutSeconds);
            ConnectionFactory factory = new()
            {
                AutomaticRecoveryEnabled = false,
                ClientProvidedName = AssemblyInfoUtilities.ApplicationName,
                NetworkRecoveryInterval = recoveryInterval,
                Password = options.Password,
                Port = options.Port,
                RequestedFrameMax = MaxFrameSize,
                RequestedHeartbeat = heartbeat,
                SocketReadTimeout = socketTimeout,
                SocketWriteTimeout = socketTimeout,
                TopologyRecoveryEnabled = false,
                UserName = options.Username,
                VirtualHost = options.VirtualHost,
                RequestedChannelMax = options.RequestedChannelMax,
            };
            PopulateClientProperties(factory);
            return factory;
        }

        /// <summary>
        /// Populates the <see cref="ConnectionFactory.ClientProperties"/> dictionary with diagnostic
        /// metadata visible in the RabbitMQ Management UI under each connection's details tab.
        /// </summary>
        /// <remarks>
        /// <para>These properties help operators identify which process, machine, and runtime version owns a connection --
        /// essential when multiple NNRPD instances or versions share the same cluster.</para>
        ///
        /// <para><b>Library defaults:</b> The RabbitMQ.Client library pre-populates <c>ClientProperties</c> with its own
        /// defaults (<c>product</c>, <c>version</c>, <c>platform</c>, <c>copyright</c>, <c>information</c>) encoded as
        /// AMQP long-strings (<c>byte[]</c>).  This method overwrites the library's <c>platform</c> with the OS version
        /// string and adds application-specific entries.  The library defaults for <c>product</c>, <c>version</c>,
        /// <c>copyright</c>, and <c>information</c> are intentionally preserved -- they identify the client library version
        /// in the Management UI.</para>
        ///
        /// <para><b>Properties set:</b></para>
        /// <list type="bullet">
        ///   <item><description><c>application</c> -- composite identifier: <c>{Name}-{Machine}-{PID}</c>
        ///     (uppercased).  Uniquely identifies this process instance in the Management UI connection
        ///     list.</description></item>
        ///   <item><description><c>application_version</c> -- entry-assembly version for deployment
        ///     tracking.</description></item>
        ///   <item><description><c>connected_at</c> -- ISO 8601 UTC timestamp of the connection attempt.  Useful for
        ///     correlating connection age with deployment events.</description></item>
        ///   <item><description><c>platform</c> -- <see cref="PlatformID"/> string (overwrites library
        ///     default).</description></item>
        ///   <item><description><c>os_version</c> -- full OS version string for environment
        ///     diagnostics.</description></item>
        ///   <item><description><c>runtime</c> -- .NET runtime description (e.g., <c>".NET 8.0.11"</c>) for runtime
        ///     version tracking.</description></item>
        ///   <item><description><c>machine</c> -- <see cref="Environment.MachineName"/> for host
        ///     identification.</description></item>
        ///   <item><description><c>process_id</c> -- current process ID for correlating with OS-level
        ///     diagnostics.</description></item>
        /// </list>
        ///
        /// <para><b>Security:</b> No credentials, tokens, or sensitive data are included in client properties.  All values
        /// are operational metadata safe for display in the RabbitMQ Management UI.</para>
        /// </remarks>
        /// <param name="factory">The factory whose properties to populate.</param>
        private static void PopulateClientProperties(ConnectionFactory factory)
        {
            int pid = Environment.ProcessId;
            string machineName = EnvironmentUtilities.ResolveMachineName();
            factory.ClientProperties["application"] = $"{AssemblyInfoUtilities.ApplicationName}-{machineName}-{pid}".ToUpperInvariant();
            factory.ClientProperties["application_version"] = AssemblyInfoUtilities.ApplicationVersion;
            factory.ClientProperties["connected_at"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            factory.ClientProperties["machine"] = machineName;
            factory.ClientProperties["os_version"] = Environment.OSVersion.VersionString;
            factory.ClientProperties["platform"] = Environment.OSVersion.Platform.ToString();
            factory.ClientProperties["process_id"] = pid.ToString(CultureInfo.InvariantCulture);
            factory.ClientProperties["runtime"] = RuntimeInformation.FrameworkDescription;
        }

        /// <summary>
        /// Builds the ordered list of <see cref="AmqpTcpEndpoint"/>s from the configured hosts, optionally with per-host
        /// TLS/SSL configuration.
        /// </summary>
        /// <remarks>
        /// <para><b>Failover ordering:</b> The RabbitMQ client tries endpoints in declaration order on initial connect and
        /// cycles through them on automatic recovery, providing transparent failover across an HA cluster.</para>
        ///
        /// <para>Each host in <see cref="RabbitMQOptions.Hosts"/> is paired with the shared
        /// <see cref="RabbitMQOptions.Port"/> to form a single <see cref="AmqpTcpEndpoint"/>.  The resulting list typically
        /// contains 1-3 entries for an Amazon MQ HA deployment.</para>
        ///
        /// <para><b>Input contract:</b> <see cref="RabbitMQOptions.Hosts"/> entries are guaranteed to be normalised
        /// (trimmed, bracket-stripped, validated for format and DNS resolution) by
        /// <see cref="RabbitMQOptions.Validate"/>.  <see cref="RabbitMQOptions.Hosts"/> is guaranteed non-empty by
        /// <see cref="System.ComponentModel.DataAnnotations.MinLengthAttribute"/> on the property.</para>
        ///
        /// <para><b>Per-host SNI:</b> When <see cref="RabbitMQOptions.EnableSsl"/> is <c>true</c>, each endpoint receives
        /// its own <see cref="SslOption"/> with <see cref="SslOption.ServerName"/> set to the individual host.  This
        /// ensures correct TLS Server Name Indication (SNI) when broker nodes use distinct certificates.  Each endpoint
        /// must have a distinct <see cref="SslOption"/> instance because <see cref="SslOption.ServerName"/> differs per
        /// host.</para>
        ///
        /// <para><b>Plaintext optimisation:</b> When <see cref="RabbitMQOptions.EnableSsl"/> is <c>false</c>, a single
        /// <see cref="SslOption"/> instance with <c>Enabled = false</c> is shared across all endpoints.  The client
        /// library ignores the <see cref="SslOption"/> entirely for plaintext connections, so sharing is safe -- and avoids
        /// allocating N identical objects for the common non-TLS deployment.</para>
        ///
        /// <para><b>Protocol restriction:</b> <see cref="SslOption.Version"/> is set to <see cref="AllowedTlsProtocols"/>
        /// (TLS 1.2 | TLS 1.3) on TLS-enabled endpoints.  For plaintext endpoints the protocol field is irrelevant -- the
        /// shared <see cref="SslOption"/> uses the library's default, which is never consulted.</para>
        /// </remarks>
        /// <param name="options">Options containing the host list, port, and SSL flag.</param>
        /// <returns>A list with one endpoint per configured host, all sharing the same
        /// <see cref="RabbitMQOptions.Port"/>.</returns>
        private List<AmqpTcpEndpoint> BuildEndpoints(RabbitMQOptions options)
        {
            string[] hosts = options.Hosts;
            int port = options.Port;
            bool enableSsl = options.EnableSsl;
            List<AmqpTcpEndpoint> endpoints = new(hosts.Length);
            // Plaintext: share a single inert SslOption across all endpoints -- the client library ignores it entirely
            // when Enabled is false, so sharing is safe and avoids N identical allocations.
            SslOption? plaintextSsl = enableSsl ? null : new SslOption { Enabled = false };
            for (int i = 0; i < hosts.Length; i++)
            {
                string host = hosts[i];
                // TLS: each endpoint needs its own SslOption for per-host SNI (ServerName differs per host).
                // Plaintext: reuse the shared instance.
                SslOption ssl = enableSsl
                    ? new SslOption
                    {
                        Enabled = true,
                        ServerName = host,
                        Version = AllowedTlsProtocols,
                    }
                    : plaintextSsl!;
                endpoints.Add(new AmqpTcpEndpoint(host, port, ssl));
            }
            if (enableSsl)
            {
                LogTlsEnabled(hosts.Length, AllowedTlsProtocols.ToString());
            }
            return endpoints;
        }

        #endregion

    }
}
