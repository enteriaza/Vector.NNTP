// RabbitMQOptions.cs -- Strongly-typed configuration for the RabbitMQ connection, bound from the "RabbitMQ" section
// in host configuration (IOptions).
//
// Required properties (Hosts, Username, Password) cause a hard-terminate at startup if missing or empty.  All other
// properties have safe defaults.
//
// Connection architecture:  A single shared IConnection is registered as a DI singleton via ConnectionPool.
// ConnectionPool establishes the connection during the host's "starting" lifecycle phase with exponential
// back-off.  Each worker creates a dedicated IChannel from the shared connection to consume article-download RPC
// requests.
//
// Validation:  Implements IValidatableObject for cross-property validation (Port/SSL consistency, host format,
// credential placeholders, VirtualHost format, and optional DNS safety checks in Production).  Invoked by
// ValidateDataAnnotations() + ValidateOnStart() in the DI options pipeline.
//
// Warning vs. error semantics:  The DI options pipeline treats every ValidationResult yielded from
// IValidatableObject.Validate as a hard startup failure -- there is no built-in "warning" severity.  To avoid
// hard-terminating on conditions that are suspicious but not definitively wrong (e.g., port/SSL mismatch, duplicate
// hosts, private IP ranges), those checks log at Warning via [LoggerMessage] source-generated methods and do NOT
// yield a ValidationResult.  Only definitive misconfigurations yield errors that block startup.
//
// Logging:  Uses [LoggerMessage] source-generated static partial methods with an explicit ILogger parameter,
// defined in RabbitMQOptions.Logging.cs.  The ILogger is resolved from the DI container via
// ValidationContext.GetService(typeof(ILoggerFactory)) during the first Validate call and cached in _logger.
// This satisfies CONTRIBUTING.md's requirement for source-generated logging while accommodating the POCO's lack
// of constructor-injected ILogger<T>.  Consistent with the RadiusOptions pattern.
//
// Cross-platform:
//   Fully portable.  All APIs used (IPAddress.TryParse, IPAddress.IsLoopback, IPAddress.TryWriteBytes,
//   FrozenSet<T>, IValidatableObject) are BCL APIs available on all .NET 8 runtimes (Windows x64, Linux x64).
//   No P/Invoke, no OS-specific APIs.
//
// SIMD applicability:
//   Not applicable.  This class performs string normalisation, DNS validation, and IP address parsing.
//   There are no contiguous memory buffers, byte-level pattern searches, or bulk numeric operations
//   that would benefit from vector instructions.
//
// Consumers:
//   ConnectionPool  -- reads Hosts, Port, Username, Password, VirtualHost, and EnableSsl to build
//                             the AMQP ConnectionFactory and endpoint list during the host's "starting"
//                             lifecycle phase.
//   ConnectionFactory      -- reads RequestedHeartbeatSeconds, NetworkRecoveryIntervalSeconds, and
//                             SocketTimeoutSeconds to configure the AMQP transport parameters.

using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Net;
using Vector.NNTP.Utilities.Networking;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.MessageBus.Configuration
{
    /// <summary>
    /// Configuration for the RabbitMQ connection, bound from the <c>"RabbitMQ"</c> section in
    /// <c>host configuration (IOptions)</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Binding:</b> Registered via
    /// <c>AddOptions&lt;RabbitMQOptions&gt;().Bind(...).ValidateDataAnnotations().ValidateOnStart()</c> in the host.
    /// Attribute-level rules use <see cref="ValidationAttribute"/>s; cross-property rules run in
    /// <see cref="RabbitMQOptionsValidator"/> via <see cref="RunCrossPropertyValidation"/>.</para>
    ///
    /// <para><b>Hard errors vs. soft warnings:</b> The DI options pipeline treats every <see cref="ValidationResult"/> as a
    /// startup-blocking failure.  Conditions that are suspicious but not definitively wrong (port/SSL mismatch, duplicate
    /// hosts, private IP ranges) are logged as warnings by <see cref="RabbitMQOptionsValidator"/> and do <em>not</em> yield a
    /// <see cref="ValidationResult"/>, allowing the application to start.</para>
    ///
    /// <para><b>Validation phases:</b></para>
    /// <list type="number">
    ///   <item><description><b>Attribute-level:</b> <see cref="RequiredAttribute"/>, <see cref="RangeAttribute"/>,
    ///     <see cref="MinLengthAttribute"/> run first via <c>ValidateDataAnnotations()</c>.  These always execute, even if
    ///     cross-property validation would also fail.</description></item>
    ///   <item><description><b>Cross-property:</b> Host format, credential placeholders, VirtualHost, DNS safety, pool
    ///     bounds, and AMQP transport invariants via <see cref="RunCrossPropertyValidation"/>.  Runs regardless of whether
    ///     attribute-level validation failed — the DI pipeline accumulates both sets of errors.</description></item>
    /// </list>
    ///
    /// <para><b>Normalisation (mutates properties in-place):</b></para>
    /// <list type="bullet">
    ///   <item><see cref="Hosts"/> -- trim whitespace (case preserved for TLS SNI).</item>
    ///   <item><see cref="Username"/> -- trim whitespace.</item>
    ///   <item><see cref="Password"/> -- trim whitespace.</item>
    ///   <item><see cref="VirtualHost"/> -- trim whitespace.</item>
    /// </list>
    ///
    /// <para><b>Logging:</b> Soft warnings and the validation success banner use <c>[LoggerMessage]</c> partial methods in
    /// the <c>RabbitMQOptions.Logging</c> partial, invoked by <see cref="RabbitMQOptionsValidator"/> with an injected
    /// <see cref="ILogger"/>.</para>
    ///
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="Connections.ConnectionPool"/> — pool sizing, lease timeouts, reconnect backoff.</item>
    ///   <item><see cref="Connections.RabbitMqConnectionFactory"/> — transport, TLS, and endpoint construction.</item>
    ///   <item><see cref="Publishing.RabbitMqPublisherPool"/> — <see cref="ChannelPoolSize"/>,
    ///     <see cref="PublishConfirmTimeout"/>, and slot acquisition.</item>
    /// </list>
    ///
    /// <para><b>Thread safety:</b> Mutable during validation (in-place normalisation). After startup validation, the
    /// options snapshot is read-only via <c>IOptions&lt;RabbitMQOptions&gt;</c>.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  All APIs used (<c>IPAddress.TryParse</c>,
    /// <see cref="IPAddress.IsLoopback"/>, <see cref="DnsValidationUtilities.ValidateHost"/>,
    /// <see cref="IPUtilities.Classify(IPAddress)"/>,
    /// <c>HostParsingUtilities.HasPortSuffix</c>,
    /// <see cref="CredentialPlaceholderDetector.IsPlaceholder"/>) are BCL APIs available on all .NET 8
    /// runtimes (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  String normalisation, DNS validation, and IP address
    /// parsing -- no contiguous memory buffers or vectorisable computation.</para>
    ///
    /// <para><b>Example configuration:</b></para>
    /// <code>
    /// {
    ///   "RabbitMQ": {
    ///       "ChannelPoolSize": 2048,
    ///       "EnableSsl": false,
    ///       "Hosts": ["rabbit01a", "rabbit01b", "rabbit01c"],
    ///       "Password": "password",
    ///       "Port": 5672,
    ///       "RpcTimeoutSeconds": 30,
    ///       "Username": "username",
    ///       "VirtualHost": "/"
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public sealed partial class RabbitMQOptions
    {

        #region Constants

        /// <summary>
        /// Configuration section key (<c>"RabbitMQ"</c>) for binding via <c>IOptions&lt;RabbitMQOptions&gt;</c>.
        /// </summary>
        public const string SectionName = "RabbitMQ";

        /// <summary>Well-known AMQP plaintext port (5672).</summary>
        private const int DefaultPlaintextPort = 5672;

        /// <summary>Well-known AMQP TLS/SSL port (5671).</summary>
        private const int DefaultSslPort = 5671;

        /// <summary>
        /// Domain-specific credential placeholder values for RabbitMQ username and password fields, found in template
        /// <c>host configuration (IOptions)</c> files.  Used by <see cref="ValidateCredentials"/> to catch copy-paste mistakes before
        /// a connection attempt fails with a cryptic authentication error.
        /// </summary>
        /// <remarks>
        /// <para>Stored as a <see cref="FrozenSet{T}"/> with <see cref="StringComparer.OrdinalIgnoreCase"/> for O(1) lookup.
        /// The set is constructed once at type-load time and reused across all validation invocations.</para>
        ///
        /// <para>These are RabbitMQ-specific placeholders (e.g., <c>"guest"</c>, <c>"your-username"</c>) that supplement
        /// the common placeholders in <see cref="CredentialPlaceholderDetector.CommonPlaceholders"/>.</para>
        /// </remarks>
        private static readonly FrozenSet<string> RabbitMQPlaceholders = FrozenSet.ToFrozenSet(
        [
            "your-username", "your-password", "guest", "user",
            "secret", "<username>", "<password>"
        ], StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Properties -- Connection

        /// <summary>
        /// One or more RabbitMQ broker hostnames or IP addresses.
        /// </summary>
        /// <remarks>
        /// <para>Required -- the application will not start if this is missing or empty.</para>
        ///
        /// <para>The RabbitMQ client maps each host to an <c>AmqpTcpEndpoint</c> and tries them in declaration order on
        /// each new TCP connect.  <see cref="Connections.ConnectionPool"/> and
        /// <see cref="Connections.RabbitMqBackgroundScaler"/> open replacement connections after faults using
        /// <see cref="Connections.HostHealthTracker"/> backoff rather than client-library automatic recovery.</para>
        ///
        /// <para>Each entry must be a bare hostname or IP address -- no URI scheme, no port suffix.  IPv6 addresses are
        /// supported (e.g., <c>"2001:db8::1"</c>).  The <see cref="Port"/> property applies to all endpoints
        /// uniformly.</para>
        ///
        /// <para>Entries are trimmed during validation but <b>not</b> lowercased -- the RabbitMQ client uses the host value
        /// for TLS SNI (<c>ServerName</c>), and some SNI implementations are case-sensitive.</para>
        /// </remarks>
        [Required(ErrorMessage = "RabbitMQ:Hosts is required.")]
        [MinLength(1, ErrorMessage = "RabbitMQ:Hosts must contain at least one host.")]
        public string[] Hosts { get; set; } = [];

        /// <summary>AMQP port shared by all endpoints in <see cref="Hosts"/>.</summary>
        /// <remarks>
        /// <para>Common values: <c>5672</c> (plaintext), <c>5671</c> (TLS/SSL).  A warning is logged during validation if
        /// the port doesn't match the <see cref="EnableSsl"/> setting.</para>
        /// </remarks>
        /// <value>Defaults to <c>5672</c>.</value>
        [Range(1, 65_535, ErrorMessage = "RabbitMQ:Port must be between 1 and 65,535.")]
        public int Port { get; set; } = DefaultPlaintextPort;

        /// <summary>RabbitMQ username for SASL PLAIN authentication.</summary>
        /// <remarks>
        /// <para>Required -- validated against <see cref="CredentialPlaceholderDetector"/> to catch common copy-paste
        /// mistakes from template appsettings files.</para>
        /// <para>Trimmed during validation to prevent authentication failures from accidental whitespace in environment
        /// variables.</para>
        /// </remarks>
        [Required(AllowEmptyStrings = false, ErrorMessage = "RabbitMQ:Username is required.")]
        public string Username { get; set; } = string.Empty;

        /// <summary>RabbitMQ password for SASL PLAIN authentication.</summary>
        /// <remarks>
        /// <para>Required -- validated against <see cref="CredentialPlaceholderDetector"/> to catch common copy-paste
        /// mistakes from template appsettings files.</para>
        /// <para>Trimmed during validation to prevent authentication failures from accidental whitespace in environment
        /// variables.</para>
        /// </remarks>
        [Required(AllowEmptyStrings = false, ErrorMessage = "RabbitMQ:Password is required.")]
        public string Password { get; set; } = string.Empty;

        /// <summary>AMQP virtual host for resource isolation.</summary>
        /// <remarks>
        /// <para>Virtual hosts provide logical separation of exchanges, queues, and bindings within a single RabbitMQ
        /// cluster.  The default <c>"/"</c> is the root vhost created automatically by RabbitMQ.  Must not be empty -- the
        /// AMQP 0-9-1 spec requires at least <c>"/"</c>.</para>
        /// <para>Trimmed during validation to prevent connection failures from accidental whitespace in environment
        /// variables.</para>
        /// </remarks>
        /// <value>Defaults to <c>"/"</c>.</value>
        public string VirtualHost { get; set; } = "/";

        /// <summary>Whether to enable TLS/SSL for the AMQP connection.</summary>
        /// <remarks>
        /// <para>When <c>true</c>, the connection uses AMQPS (typically port 5671).  The connection factory configures
        /// <c>SslOption</c> with the first host as the SNI server name.</para>
        /// <para>When <c>false</c>, the connection uses plaintext AMQP (typically port 5672).</para>
        /// </remarks>
        /// <value>Defaults to <c>false</c>.</value>
        public bool EnableSsl { get; set; }

        #endregion

        #region Properties -- Application

        /// <summary>Timeout in seconds for an in-flight RPC article fetch before the request is considered failed.</summary>
        /// <remarks>
        /// <para>Bounds the total time from issuing the NNTP <c>ARTICLE</c> command to receiving the complete multi-line
        /// response.  Large articles on slow NNTP servers may require a higher value.</para>
        /// </remarks>
        /// <value>Defaults to <c>30</c> seconds.</value>
        [Range(1, 300, ErrorMessage = "RabbitMQ:RpcTimeoutSeconds must be between 1 and 300.")]
        public int RpcTimeoutSeconds { get; set; } = 30;

        /// <summary>Number of channels to pre-create in the channel pool.</summary>
        /// <remarks>
        /// <para>AMQP channels are lightweight multiplexed streams over a single TCP connection.  Pre-creating them avoids
        /// the latency of on-demand channel creation during the first article fetch.</para>
        /// </remarks>
        /// <value>Defaults to <c>2047</c>.</value>
        [Range(1, 65534, ErrorMessage = "RabbitMQ:ChannelPoolSize must be between 1 and 65534.")]
        public ushort ChannelPoolSize { get; set; } = 2047;

        /// <summary>
        /// Maximum consecutive <see cref="RabbitMQ.Client.IConnection.ConnectionRecoveryErrorAsync"/> events before
        /// <see cref="Connections.RabbitMqConnectionFactory"/> calls <see cref="IHostApplicationLifetime.StopApplication"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Pool-managed recovery:</b> TCP healing is normally performed by <see cref="Connections.ConnectionPool"/>
        /// and <see cref="Connections.RabbitMqBackgroundScaler"/> because client-library automatic recovery is disabled in
        /// <see cref="Connections.RabbitMqConnectionFactory.CreateFactory"/>.</para>
        ///
        /// <para><b>Fail-fast hook:</b> This threshold guards the dormant client-library recovery error handler path. If
        /// automatic recovery were enabled and repeated <c>ConnectionRecoveryErrorAsync</c> events indicated an unrecoverable
        /// zombie connection, the factory logs at <see cref="LogLevel.Critical"/> and stops the host so systemd/Kubernetes
        /// can restart the process with a clean TCP handshake.</para>
        ///
        /// <para><b>Default:</b> 10 consecutive handler events.  With the default <see cref="NetworkRecoveryIntervalSeconds"/>
        /// of 5 seconds, that is roughly 50 seconds of continuous recovery failure before shutdown.</para>
        ///
        /// <para><b>Disable:</b> Set to <c>0</c> to disable the fail-fast hook.  Use only when an external liveness probe
        /// owns zombie detection.</para>
        ///
        /// <para><b>Consumer:</b> Evaluated in <c>RabbitMqConnectionFactory.AttachConnectionEventHandlers</c> on
        /// <see cref="RabbitMQ.Client.IConnection.ConnectionRecoveryErrorAsync"/>.</para>
        /// </remarks>
        /// <value>Defaults to <c>10</c>.</value>
        [Range(0, 1000, ErrorMessage = "RabbitMQ:MaxConsecutiveRecoveryFailures must be between 0 (disabled) and 1,000.")]
        public int MaxConsecutiveRecoveryFailures { get; set; } = 10;

        #endregion

        #region Properties -- TCP pool and scaling

        /// <summary>Minimum long-lived TCP connections maintained by the pool.</summary>
        /// <remarks>Consumed by <see cref="Connections.ConnectionPool.StartAsync"/>.</remarks>
        /// <value>Defaults to <c>1</c>.</value>
        [Range(1, 64, ErrorMessage = "RabbitMQ:MinConnections must be between 1 and 64.")]
        public int MinConnections { get; set; } = 1;

        /// <summary>Maximum long-lived TCP connections the background scaler may create.</summary>
        /// <remarks>Consumed by <see cref="Connections.RabbitMqBackgroundScaler"/>.</remarks>
        /// <value>Defaults to <c>4</c>.</value>
        [Range(1, 64, ErrorMessage = "RabbitMQ:MaxConnections must be between 1 and 64.")]
        public int MaxConnections { get; set; } = 4;

        /// <summary>AMQP negotiated maximum channels per TCP connection.</summary>
        /// <remarks>Must be greater than or equal to <see cref="ChannelPoolSize"/>.</remarks>
        /// <value>Defaults to <c>2048</c>.</value>
        [Range(1, 65534, ErrorMessage = "RabbitMQ:RequestedChannelMax must be between 1 and 65534.")]
        public ushort RequestedChannelMax { get; set; } = 2048;

        /// <summary>Minimum lifetime before a connection is eligible for scale-down.</summary>
        /// <remarks>Prevents churn when load is bursty.</remarks>
        /// <value>Defaults to 5 minutes.</value>
        public TimeSpan MinimumConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>Cooldown after scale-down before another scale-down is allowed.</summary>
        /// <value>Defaults to 2 minutes.</value>
        public TimeSpan ScaleDownCooldown { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>Idle duration in seconds before a connection is considered for scale-down.</summary>
        /// <value>Defaults to <c>600</c> seconds.</value>
        [Range(30, 86_400, ErrorMessage = "RabbitMQ:ConnectionScaleDownIdleSeconds must be between 30 and 86,400.")]
        public int ConnectionScaleDownIdleSeconds { get; set; } = 600;

        /// <summary>Duration a connection may remain broker-blocked before quarantine (stalled).</summary>
        /// <remarks>
        /// <para>Consumed by <see cref="Connections.RabbitMqPoolFlowControlMonitor"/>.</para>
        /// <para>Blocked is transient; stalled excludes the connection from new publisher slots without faulting TCP.</para>
        /// </remarks>
        /// <value>Defaults to 60 seconds.</value>
        public TimeSpan ConnectionBlockedTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Maximum wait time to acquire a publisher slot.</summary>
        /// <remarks>Maps to <see cref="Exceptions.MessageBusLeaseTimeoutException"/> when exceeded.</remarks>
        /// <value>Defaults to 30 seconds.</value>
        public TimeSpan ChannelLeaseTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Maximum concurrent waiters for publisher slots.</summary>
        /// <remarks>Additional waiters receive <see cref="Exceptions.MessageBusUnavailableException"/>.</remarks>
        /// <value>Defaults to <c>4096</c>.</value>
        [Range(1, 100_000, ErrorMessage = "RabbitMQ:MaxPendingLeaseWaiters must be between 1 and 100,000.")]
        public int MaxPendingLeaseWaiters { get; set; } = 4096;

        /// <summary>Per-publish publisher confirm wait timeout (RabbitMQ.Client 7).</summary>
        /// <remarks>
        /// <para>Also bounds confirm wait during scope disposal and is an operational upper bound during host shutdown.</para>
        /// </remarks>
        /// <value>Defaults to 30 seconds.</value>
        public TimeSpan PublishConfirmTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Upper bound for pool-wide shutdown drain.</summary>
        /// <remarks>Consumed by <see cref="Connections.RabbitMqPoolSupervisor.StopAsync"/>.</remarks>
        /// <value>Defaults to 2 minutes.</value>
        public TimeSpan MaximumShutdownDrainTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>Base delay for pool-managed reconnect backoff.</summary>
        /// <remarks>Used with full jitter up to <see cref="PoolReconnectMaxDelayMs"/>.</remarks>
        /// <value>Defaults to <c>1000</c> ms.</value>
        [Range(100, 60_000, ErrorMessage = "RabbitMQ:PoolReconnectBaseDelayMs must be between 100 and 60,000.")]
        public int PoolReconnectBaseDelayMs { get; set; } = 1000;

        /// <summary>Maximum delay for pool-managed reconnect backoff.</summary>
        /// <value>Defaults to <c>30000</c> ms.</value>
        [Range(1000, 300_000, ErrorMessage = "RabbitMQ:PoolReconnectMaxDelayMs must be between 1,000 and 300,000.")]
        public int PoolReconnectMaxDelayMs { get; set; } = 30_000;

        /// <summary>Fraction of connections faulted before health is Degraded.</summary>
        /// <remarks>Must be less than or equal to <see cref="UnhealthyThreshold"/>.</remarks>
        /// <value>Defaults to <c>0.25</c>.</value>
        [Range(0.01, 1.0, ErrorMessage = "RabbitMQ:DegradedThreshold must be between 0.01 and 1.0.")]
        public double DegradedThreshold { get; set; } = 0.25;

        /// <summary>Fraction of connections faulted before health is Unhealthy.</summary>
        /// <value>Defaults to <c>0.75</c>.</value>
        [Range(0.01, 1.0, ErrorMessage = "RabbitMQ:UnhealthyThreshold must be between 0.01 and 1.0.")]
        public double UnhealthyThreshold { get; set; } = 0.75;

        #endregion

        #region Properties -- AMQP Transport

        /// <summary>
        /// AMQP heartbeat interval in seconds, negotiated with the broker during connection establishment.
        /// </summary>
        /// <remarks>
        /// <para>The broker declares the connection dead after 2x this value.  At the default of 15 seconds, the dead-
        /// connection timeout is 30 seconds.  Lower values increase sensitivity to transient network jitter; higher values
        /// delay dead-connection detection and leave the broker holding unacked messages longer.</para>
        ///
        /// <para>The value is <em>negotiated</em>: the broker takes the lower of the client's requested value and its own
        /// configured <c>heartbeat</c> setting.  If the broker's <c>heartbeat</c> is set to 10 and this property is 15,
        /// the effective heartbeat will be 10.</para>
        ///
        /// <para><b>Cross-property invariant:</b> <see cref="SocketTimeoutSeconds"/> must be >= 2x this value.  If the
        /// socket timeout is shorter than the dead-connection detection window, the socket will time out before the
        /// heartbeat mechanism can detect the failure -- causing spurious disconnections during normal idle periods.
        /// This invariant is enforced in <see cref="ValidateTransportParameters"/>.</para>
        ///
        /// <para><b>Consumer:</b> Read by <see cref="Connections.RabbitMqConnectionFactory"/> and applied to
        /// <see cref="RabbitMQ.Client.ConnectionFactory.RequestedHeartbeat"/>.</para>
        /// </remarks>
        /// <value>Defaults to <c>15</c> seconds (dead-connection timeout = 30 s).</value>
        [Range(5, 120, ErrorMessage = "RabbitMQ:RequestedHeartbeatSeconds must be between 5 and 120.")]
        public int RequestedHeartbeatSeconds { get; set; } = 15;

        /// <summary>
        /// Interval in seconds assigned to <see cref="RabbitMQ.Client.ConnectionFactory.NetworkRecoveryInterval"/> and
        /// referenced in shutdown/recovery log messages.
        /// </summary>
        /// <remarks>
        /// <para><b>Pool-managed recovery:</b> Active reconnect backoff for new TCP connections uses
        /// <see cref="PoolReconnectBaseDelayMs"/> and <see cref="PoolReconnectMaxDelayMs"/> via
        /// <see cref="Connections.HostHealthTracker"/>, not this property.</para>
        ///
        /// <para><b>Factory setting:</b> Still applied to <see cref="RabbitMQ.Client.ConnectionFactory"/> for API
        /// completeness and for recovery-event log context.  Client-library automatic recovery is disabled today, so this
        /// interval does not drive live reconnect loops unless factory recovery flags change.</para>
        ///
        /// <para><b>Consumer:</b> Read by <see cref="Connections.RabbitMqConnectionFactory.CreateFactory"/>.</para>
        /// </remarks>
        /// <value>Defaults to <c>5</c> seconds.</value>
        [Range(1, 60, ErrorMessage = "RabbitMQ:NetworkRecoveryIntervalSeconds must be between 1 and 60.")]
        public int NetworkRecoveryIntervalSeconds { get; set; } = 5;

        /// <summary>
        /// TCP socket read/write timeout in seconds.  Prevents indefinite hangs when the remote broker becomes unreachable
        /// without sending a TCP RST (e.g., silent network partition, firewall drop).
        /// </summary>
        /// <remarks>
        /// <para>Applied to both <see cref="RabbitMQ.Client.ConnectionFactory.SocketReadTimeout"/> and
        /// <see cref="RabbitMQ.Client.ConnectionFactory.SocketWriteTimeout"/>.</para>
        ///
        /// <para><b>Cross-property invariant:</b> Must be >= 2x <see cref="RequestedHeartbeatSeconds"/>.  At the default
        /// of 30 seconds (with heartbeat = 15 s), the socket timeout aligns exactly with the broker's dead-connection
        /// detection window (2x heartbeat).  If the socket timeout were shorter, idle connections would be torn down by
        /// the socket layer before the AMQP heartbeat mechanism could keep them alive.</para>
        ///
        /// <para><b>Consumer:</b> Read by <see cref="Connections.RabbitMqConnectionFactory"/> and applied to
        /// <see cref="RabbitMQ.Client.ConnectionFactory.SocketReadTimeout"/> and
        /// <see cref="RabbitMQ.Client.ConnectionFactory.SocketWriteTimeout"/>.</para>
        /// </remarks>
        /// <value>Defaults to <c>30</c> seconds (= 2x default heartbeat).</value>
        [Range(10, 300, ErrorMessage = "RabbitMQ:SocketTimeoutSeconds must be between 10 and 300.")]
        public int SocketTimeoutSeconds { get; set; } = 30;

        #endregion

        #region Internal Methods -- Cross-property validation (invoked by RabbitMQOptionsValidator)

        /// <summary>
        /// Normalises string properties and collects hard validation errors.  Soft warnings and success logging are
        /// performed by <see cref="RabbitMQOptionsValidator"/>.
        /// </summary>
        /// <param name="logger">Logger for production-safety warnings during host validation.</param>
        /// <param name="hostEnvironment">Host environment; production checks run when
        /// <see cref="HostEnvironmentEnvExtensions.IsProduction(IHostEnvironment)"/>.</param>
        /// <param name="errors">Accumulator for hard validation errors returned to the validator.</param>
        internal void RunCrossPropertyValidation(ILogger logger, IHostEnvironment? hostEnvironment, List<ValidationResult> errors)
        {
            bool isProduction = hostEnvironment?.IsProduction() ?? false;
            if (Hosts is not null)
            {
                for (int i = 0; i < Hosts.Length; i++)
                {
                    if (Hosts[i] is not null)
                        Hosts[i] = Hosts[i].Trim();
                }
            }
            Username = Username?.Trim() ?? string.Empty;
            Password = Password?.Trim() ?? string.Empty;
            VirtualHost = VirtualHost?.Trim() ?? string.Empty;
            ValidateHosts(errors, isProduction, logger);
            ValidateCredentials(errors);
            ValidateVirtualHost(errors);
            ValidateTransportParameters(errors);
            ValidatePoolParameters(errors);
        }

        /// <summary>Validates <see cref="Username"/> and <see cref="Password"/> are not template placeholders.</summary>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        private void ValidateCredentials(List<ValidationResult> errors)
        {
            if (CredentialPlaceholderDetector.IsPlaceholder(Username, RabbitMQPlaceholders))
            {
                errors.Add(new ValidationResult(
                    "RabbitMQ:Username appears to be a template placeholder.",
                    [nameof(Username)]));
            }
            if (CredentialPlaceholderDetector.IsPlaceholder(Password, RabbitMQPlaceholders))
            {
                errors.Add(new ValidationResult(
                    "RabbitMQ:Password appears to be a template placeholder.",
                    [nameof(Password)]));
            }
        }

        /// <summary>Validates TCP pool sizing and health threshold cross-property invariants.</summary>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        private void ValidatePoolParameters(List<ValidationResult> errors)
        {
            if (MinConnections > MaxConnections)
            {
                errors.Add(new ValidationResult(
                    $"MinConnections ({MinConnections}) must not exceed MaxConnections ({MaxConnections}).",
                    [nameof(MinConnections), nameof(MaxConnections)]));
            }
            if (DegradedThreshold > UnhealthyThreshold)
            {
                errors.Add(new ValidationResult(
                    "DegradedThreshold must be less than or equal to UnhealthyThreshold.",
                    [nameof(DegradedThreshold), nameof(UnhealthyThreshold)]));
            }
            if (ChannelPoolSize > RequestedChannelMax)
            {
                errors.Add(new ValidationResult(
                    $"ChannelPoolSize ({ChannelPoolSize}) must not exceed RequestedChannelMax ({RequestedChannelMax}).",
                    [nameof(ChannelPoolSize), nameof(RequestedChannelMax)]));
            }
        }

        /// <summary>
        /// Emits port/SSL and duplicate-host warnings (call at most once per process startup validation cycle).
        /// </summary>
        /// <param name="logger">Logger for advisory warnings.</param>
        internal void EmitSoftWarnings(ILogger logger)
        {
            WarnOnPortSslMismatch(logger);
            WarnOnDuplicateHosts(logger);
        }

        /// <summary>
        /// Emits the validation success summary when there are zero hard errors.
        /// </summary>
        /// <param name="logger">Logger for the startup banner.</param>
        internal void EmitValidationSuccessSummary(ILogger logger)
        {
            LogValidationSuccess(logger, Hosts?.Length ?? 0, Port, EnableSsl, VirtualHost, RpcTimeoutSeconds,
                ChannelPoolSize, RequestedHeartbeatSeconds, NetworkRecoveryIntervalSeconds, SocketTimeoutSeconds,
                MaxConsecutiveRecoveryFailures);
        }

        #endregion

        #region Private Methods -- Host Validation

        /// <summary>
        /// Validates all entries in <see cref="Hosts"/>: format checks (URI schemes, port suffixes, whitespace), DNS
        /// resolution, production safety (loopback, private ranges), and duplicate detection.
        /// </summary>
        /// <remarks>
        /// <para>The DI options pipeline runs attribute-level validation first, then calls
        /// cross-property validation regardless of whether attribute-level errors occurred — results from both phases are
        /// accumulated.  This means <see cref="Hosts"/> could be <see langword="null"/> or empty when this method executes
        /// (if <see cref="RequiredAttribute"/> or <see cref="MinLengthAttribute"/> failed), so the <see langword="null"/>
        /// guard is necessary.</para>
        /// </remarks>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        /// <param name="isProduction"><see langword="true"/> when running in the Production environment; enables DNS safety
        /// checks.</param>
        /// <param name="logger">Logger for production host-range advisories.</param>
        private void ValidateHosts(List<ValidationResult> errors, bool isProduction, ILogger logger)
        {
            if (Hosts is null)
                return;
            for (int i = 0; i < Hosts.Length; i++)
            {
                string host = Hosts[i];
                if (string.IsNullOrWhiteSpace(host))
                {
                    errors.Add(new ValidationResult(
                        $"Hosts[{i}] is null or empty.",
                        [nameof(Hosts)]));
                    continue;
                }
                if (HostParsingUtilities.HasUriScheme(host))
                {
                    errors.Add(new ValidationResult(
                        $"Hosts[{i}] ('{host}') must not contain a URI scheme (e.g., 'amqps://'). " +
                        "Provide only the hostname or IP address.",
                        [nameof(Hosts)]));
                    continue;
                }

                // Strip RFC 5952 section 6 bracket notation from IPv6 literals.  Configuration files and environment
                // variables sometimes contain bracket-wrapped IPv6 addresses copied from URLs (e.g., "[2001:db8::1]").
                // The brackets are a URI presentation format, not part of the address itself.  Leaving them in place
                // would cause:
                //   1. FormatHostWithPort to produce "[[2001:db8::1]]:5672" (double-bracketed).
                //   2. AmqpTcpEndpoint to receive a bracket-wrapped hostname, which may fail DNS resolution depending on
                //      the RabbitMQ client library version and the underlying socket implementation.
                // Normalisation is applied in-place (same pattern as the whitespace trim in Validate) so downstream
                // consumers (ConnectionFactory, endpoint builders) never see the brackets.
                host = HostParsingUtilities.StripIPv6Brackets(host)!;
                Hosts[i] = host;
                if (HostParsingUtilities.HasPortSuffix(host))
                {
                    errors.Add(new ValidationResult(
                        $"Hosts[{i}] ('{host}') must not contain a port suffix. Use the Port property instead.",
                        [nameof(Hosts)]));
                    continue;
                }
                if (!IPAddress.TryParse(host, out _) && !DnsValidationUtilities.ValidateHost(host, out string? dnsError))
                {
                    errors.Add(new ValidationResult(
                        $"Hosts[{i}] ('{host}'): {dnsError}",
                        [nameof(Hosts)]));
                    continue;
                }
                if (isProduction)
                    ValidateHostProductionSafety(errors, host, i, logger);
            }
        }

        /// <summary>
        /// Validates a single host entry for production safety: rejects <c>localhost</c> and loopback addresses as hard
        /// errors, and logs warnings for private/reserved IP ranges and IPv6 link-local.
        /// </summary>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        /// <param name="trimmedHost">The whitespace-trimmed host string.</param>
        /// <param name="index">Zero-based index in the <see cref="Hosts"/> array for error messages.</param>
        /// <param name="logger">Logger for private-range and link-local advisories.</param>
        /// <remarks>
        /// <para><b>Loopback (hard error):</b> <c>127.0.0.0/8</c> and <c>::1</c> are rejected -- a production broker
        /// should never be on loopback.</para>
        ///
        /// <para><b>localhost (hard error):</b> Rejected explicitly because
        /// <see cref="IPAddress.TryParse(string, out IPAddress)"/> returns <see langword="false"/> for <c>"localhost"</c>,
        /// so it bypasses the loopback IP check.</para>
        ///
        /// <para><b>Private/reserved ranges (soft warning):</b> RFC 1918, RFC 6598 CGN, link-local, and IPv6 link-local
        /// are logged but do NOT block startup -- they could be intentional in VPN/VPC scenarios.</para>
        ///
        /// <para><b>Hostnames:</b> Entries that do not parse as an IP address (DNS hostnames) are not checked for range
        /// safety here -- resolving them to IP addresses for range classification would require an additional DNS lookup
        /// beyond the one already performed by <see cref="DnsValidationUtilities.ValidateHost"/>.</para>
        /// </remarks>
        private void ValidateHostProductionSafety(List<ValidationResult> errors, string trimmedHost, int index, ILogger logger)
        {
            if (trimmedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationResult(
                    $"RabbitMQ:Hosts[{index}] is 'localhost'. Production RabbitMQ brokers must not use localhost. " +
                    "Use the actual broker hostname or IP address.",
                    [nameof(Hosts)]));
                return;
            }
            if (!IPAddress.TryParse(trimmedHost, out IPAddress? address))
                return;
            if (IPAddress.IsLoopback(address))
            {
                errors.Add(new ValidationResult(
                    $"RabbitMQ:Hosts[{index}] ('{trimmedHost}') resolves to a loopback address. " +
                    "Production RabbitMQ brokers must not use loopback (127.0.0.0/8 or ::1). " +
                    "Use the actual broker hostname or IP address.",
                    [nameof(Hosts)]));
                return;
            }
            if (address.IsIPv6LinkLocal)
            {
                LogIPv6LinkLocal(logger, index, trimmedHost);
                return;
            }
            // Classify private IPv4 ranges using the shared utility.
            string? rangeDescription = IPUtilities.Classify(address);
            if (rangeDescription is not null)
                LogPrivateIpRange(logger, index, trimmedHost, rangeDescription);
        }

        #endregion

        #region Private Methods -- Credential & VirtualHost Validation

        /// <summary>
        /// Validates that <see cref="VirtualHost"/> is non-empty, as required by the AMQP 0-9-1 specification.
        /// </summary>
        /// <remarks>
        /// <para><see cref="VirtualHost"/> is already trimmed by <see cref="RunCrossPropertyValidation"/> before this method
        /// is called.</para>
        /// </remarks>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        private void ValidateVirtualHost(List<ValidationResult> errors)
        {
            if (string.IsNullOrWhiteSpace(VirtualHost))
            {
                errors.Add(new ValidationResult(
                    "VirtualHost must not be empty. Use \"/\" for the default virtual host.",
                    [nameof(VirtualHost)]));
            }
        }

        #endregion

        #region Private Methods -- Transport Parameter Validation

        /// <summary>
        /// Validates cross-property invariants between AMQP transport parameters.
        /// </summary>
        /// <remarks>
        /// <para><b>Invariant:</b> <see cref="SocketTimeoutSeconds"/> must be >= 2x <see cref="RequestedHeartbeatSeconds"/>.
        /// The AMQP heartbeat mechanism detects dead connections after 2x the heartbeat interval (the "missed heartbeat"
        /// window).  If the TCP socket read timeout is shorter than this window, the socket layer will tear down idle
        /// connections before the AMQP heartbeat can keep them alive -- causing spurious disconnections during normal idle
        /// periods where no application data is flowing.</para>
        ///
        /// <para><b>Example:</b> With <c>RequestedHeartbeatSeconds = 15</c>, the minimum safe socket timeout is 30 s.
        /// Setting <c>SocketTimeoutSeconds = 20</c> would cause: heartbeat sent at T+15 -> response expected by T+30 ->
        /// but socket times out at T+20 -> connection torn down prematurely.</para>
        ///
        /// <para>Individual range validation is handled by <see cref="RangeAttribute"/>s on each property.  This method
        /// only checks relationships between properties.</para>
        /// </remarks>
        /// <param name="errors">Accumulator for hard validation errors.</param>
        private void ValidateTransportParameters(List<ValidationResult> errors)
        {
            int minimumSocketTimeout = RequestedHeartbeatSeconds * 2;
            if (SocketTimeoutSeconds < minimumSocketTimeout)
            {
                errors.Add(new ValidationResult(
                    $"SocketTimeoutSeconds ({SocketTimeoutSeconds}) must be at least 2x RequestedHeartbeatSeconds " +
                    $"({RequestedHeartbeatSeconds} x 2 = {minimumSocketTimeout}). " +
                    "A shorter socket timeout causes spurious disconnections because the TCP socket times out before " +
                    "the AMQP heartbeat mechanism can detect and recover the connection.",
                    [nameof(SocketTimeoutSeconds), nameof(RequestedHeartbeatSeconds)]));
            }
        }

        #endregion

        #region Private Methods -- Soft Warnings

        /// <summary>
        /// Logs a warning if the <see cref="Port"/> and <see cref="EnableSsl"/> values are inconsistent with well-known
        /// AMQP port conventions.
        /// </summary>
        /// <remarks>
        /// <para>Not a hard error because non-standard port/SSL combinations are legitimate in some environments (e.g.,
        /// AMQPS on a custom port behind a TLS-terminating load balancer).</para>
        ///
        /// <para><b>Warning deduplication:</b> Called at most once per process by <see cref="RabbitMQOptionsValidator"/>.</para>
        /// </remarks>
        /// <param name="logger">Logger for port/SSL advisories.</param>
        private void WarnOnPortSslMismatch(ILogger logger)
        {
            if (EnableSsl && Port == DefaultPlaintextPort)
                LogSslPlaintextPortMismatch(logger, DefaultPlaintextPort, DefaultSslPort);
            if (!EnableSsl && Port == DefaultSslPort)
                LogPlaintextSslPortMismatch(logger, DefaultSslPort);
        }

        /// <summary>
        /// Logs a warning for each duplicate entry in <see cref="Hosts"/> that reduces effective failover capacity.
        /// </summary>
        /// <remarks>
        /// <para>Duplicate hosts are not invalid -- the RabbitMQ client library will simply try the same endpoint twice
        /// during failover, which is wasteful but not harmful.</para>
        /// <para>Host entries are already trimmed in-place by the normalisation step in
        /// <see cref="RunCrossPropertyValidation"/> before this
        /// method is called by <see cref="RunCrossPropertyValidation"/>, so no additional trimming is needed here.</para>
        ///
        /// <para><b>Warning deduplication:</b> Called at most once per process by <see cref="RabbitMQOptionsValidator"/>.</para>
        /// </remarks>
        /// <param name="logger">Logger for duplicate-host advisories.</param>
        private void WarnOnDuplicateHosts(ILogger logger)
        {
            if (Hosts is null)
                return;
            HashSet<string> seen = new(Hosts.Length, StringComparer.OrdinalIgnoreCase);
            foreach (string host in Hosts)
            {
                if (string.IsNullOrWhiteSpace(host))
                    continue;
                if (!seen.Add(host))
                    LogDuplicateHost(logger, host);
            }
        }

        #endregion

    }
}
