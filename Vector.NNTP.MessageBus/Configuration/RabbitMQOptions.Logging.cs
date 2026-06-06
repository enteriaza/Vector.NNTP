// RabbitMQOptions.Logging.cs -- Source-generated [LoggerMessage] partial methods for RabbitMQOptions validation.
//
// Contains all log events used by RabbitMQOptions.Validate and its private helper methods.  Separated from
// the main options file per CONTRIBUTING.md "Source-Generated Logging with [LoggerMessage]" convention.
//
// The log methods use an explicit ILogger? parameter because RabbitMQOptions is a POCO options type that
// does not hold a logger field via DI constructor injection.  The ILogger is resolved from the DI container
// via ValidationContext.GetService(typeof(ILoggerFactory)) during the first Validate call and cached in the
// static _logger field.  Consistent with the RadiusOptions.Logging.cs pattern.
//
// All [LoggerMessage] Message strings use ASCII-only characters per CONTRIBUTING.md.
//
// Callers:
//   RabbitMQOptions.Validate                    -- success summary (53).
//   RabbitMQOptions.WarnOnPortSslMismatch       -- port/SSL consistency advisory (54, 55).
//   RabbitMQOptions.WarnOnDuplicateHosts        -- duplicate host advisory (56).
//   RabbitMQOptions.ValidateHostProductionSafety -- IPv6 link-local (57), private range (58) advisories.
//
// EventId range allocation:
//   validator: 50-59.
//
// Log level policy (aligned with CONTRIBUTING.md Log Levels):
//   Information -- Successful validation summary (startup banner).
//   Warning     -- Port/SSL mismatch, duplicate hosts, private/reserved IP ranges, IPv6 link-local.
//
// Security:
//   No method logs Password or any credential material.  Only non-sensitive operational metadata
//   (host count, port, SSL flag, VHost, timeout values) is logged.
//
// Thread safety:
//   All methods are static partial methods.  The source-generated implementations perform a null check
//   on the ILogger parameter before invoking IsEnabled, so passing null is safe and results in a no-op.
//
// Cross-platform:
//   Fully portable across Windows (x64) and Linux (x64) on .NET 8.  Source-generated logging uses BCL
//   Microsoft.Extensions.Logging APIs.  No P/Invoke, no OS-specific APIs.
//
// SIMD applicability:
//   Not applicable.  Log method stubs are compile-time generated; no runtime data processing occurs
//   in these declarations.

namespace Vector.NNTP.MessageBus.Configuration
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMQOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 50-59 -- reserved for <see cref="RabbitMQOptions"/> validation.</para>
    ///
    /// <para><b>Pattern:</b> Each method is a <see langword="static"/> <see langword="partial"/> method annotated
    /// with <see cref="LoggerMessageAttribute"/> and an explicit <see cref="ILogger"/> parameter.  The source
    /// generator emits the implementation at compile time, providing zero-allocation logging when the log level is
    /// disabled and compile-time validation of message templates.</para>
    ///
    /// <para><b>Null-safe invocation:</b> All callers pass the <see cref="ILogger"/> resolved by
    /// <see cref="RabbitMQOptionsValidator"/>.  The
    /// source-generated implementation performs a null check on the <see cref="ILogger"/> parameter before
    /// invoking <see cref="ILogger.IsEnabled(LogLevel)"/>, so passing <see langword="null"/> is safe and
    /// results in a no-op.</para>
    /// </remarks>
    public sealed partial class RabbitMQOptions
    {

        #region Logging -- Validation Success (53)

        /// <summary>
        /// Logs the successful validation summary with all effective RabbitMQ configuration values.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="RabbitMQOptionsValidator.Validate(string?, RabbitMQOptions)"/> -- emitted exactly once
        /// (guarded by <see cref="RabbitMQOptionsValidator"/>'s one-shot success flag) after all validation checks pass with zero errors.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> because successful
        /// configuration validation is a startup banner event per CONTRIBUTING.md log levels.</para>
        ///
        /// <para><b>Security:</b> Does not log <see cref="Password"/> or any credential material.  Only non-sensitive
        /// operational metadata (host count, port, SSL, VHost, timeouts, pool size) is included.</para>
        /// </remarks>
        /// <param name="logger">The cached <see cref="ILogger"/> resolved from the DI container, or
        /// <see langword="null"/> if unavailable.</param>
        /// <param name="hostCount">The number of validated RabbitMQ hosts.</param>
        /// <param name="port">The configured AMQP port.</param>
        /// <param name="enableSsl">Whether TLS/SSL is enabled.</param>
        /// <param name="virtualHost">The configured AMQP virtual host.</param>
        /// <param name="rpcTimeoutSeconds">The configured RPC timeout in seconds.</param>
        /// <param name="channelPoolSize">The configured channel pool size.</param>
        /// <param name="heartbeatSeconds">The configured heartbeat interval in seconds.</param>
        /// <param name="recoveryIntervalSeconds">The configured network recovery interval in seconds.</param>
        /// <param name="socketTimeoutSeconds">The configured socket timeout in seconds.</param>
        /// <param name="maxConsecutiveRecoveryFailures">The configured maximum consecutive recovery failures.</param>
        [LoggerMessage(EventId = 53, Level = LogLevel.Information,
            Message = "RabbitMQ configuration validated -- Hosts={HostCount}, Port={Port}, SSL={EnableSsl}, " +
                      "VHost={VirtualHost}, RpcTimeout={RpcTimeoutSeconds}s, ChannelPoolSize={ChannelPoolSize}, " +
                      "Heartbeat={HeartbeatSeconds}s, RecoveryInterval={RecoveryIntervalSeconds}s, " +
                      "SocketTimeout={SocketTimeoutSeconds}s, MaxConsecutiveRecoveryFailures={MaxConsecutiveRecoveryFailures}")]
        internal static partial void LogValidationSuccess(ILogger logger, int hostCount, int port, bool enableSsl,
            string virtualHost, int rpcTimeoutSeconds, int channelPoolSize, int heartbeatSeconds,
            int recoveryIntervalSeconds, int socketTimeoutSeconds, int maxConsecutiveRecoveryFailures);

        #endregion

        #region Logging -- Port/SSL Mismatch Warnings (54, 55)

        /// <summary>
        /// Logs a warning when <see cref="EnableSsl"/> is <c>true</c> but <see cref="Port"/> is the standard plaintext
        /// AMQP port.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="WarnOnPortSslMismatch"/> -- emitted exactly once (guarded by
        /// <see cref="RabbitMQOptionsValidator"/>'s one-shot warning flag) when the SSL flag and port are inconsistent.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because the TLS handshake
        /// will likely fail on the plaintext port, but non-standard configurations are valid in some environments.</para>
        /// </remarks>
        /// <param name="logger">The cached <see cref="ILogger"/> resolved from the DI container, or
        /// <see langword="null"/> if unavailable.</param>
        /// <param name="port">The standard plaintext AMQP port (5672).</param>
        /// <param name="sslPort">The standard AMQPS port (5671).</param>
        [LoggerMessage(EventId = 54, Level = LogLevel.Warning,
            Message = "RabbitMQ:EnableSsl is true but Port is {Port} (standard plaintext AMQP). " +
                      "The TLS handshake will likely fail. Did you mean to use port {SslPort}?")]
        internal static partial void LogSslPlaintextPortMismatch(ILogger logger, int port, int sslPort);

        /// <summary>
        /// Logs a warning when <see cref="EnableSsl"/> is <c>false</c> but <see cref="Port"/> is the standard AMQPS port.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="WarnOnPortSslMismatch"/> -- emitted exactly once (guarded by
        /// <see cref="RabbitMQOptionsValidator"/>'s one-shot warning flag) when the SSL flag and port are inconsistent.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because a plaintext connection
        /// to the TLS port will likely fail, but non-standard configurations are valid in some environments.</para>
        /// </remarks>
        /// <param name="logger">The cached <see cref="ILogger"/> resolved from the DI container, or
        /// <see langword="null"/> if unavailable.</param>
        /// <param name="port">The standard AMQPS port (5671).</param>
        [LoggerMessage(EventId = 55, Level = LogLevel.Warning,
            Message = "RabbitMQ:EnableSsl is false but Port is {Port} (standard AMQPS). " +
                      "The connection will likely fail. Did you mean to set EnableSsl to true?")]
        internal static partial void LogPlaintextSslPortMismatch(ILogger logger, int port);

        #endregion

        #region Logging -- Duplicate Host Warning (56)

        /// <summary>
        /// Logs a warning when a duplicate host entry is detected in <see cref="Hosts"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="WarnOnDuplicateHosts"/> -- emitted exactly once per duplicate (guarded by
        /// <see cref="RabbitMQOptionsValidator"/>'s one-shot warning flag) when the same host appears more than once.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because duplicate hosts
        /// reduce effective failover capacity but are not technically invalid.</para>
        /// </remarks>
        /// <param name="logger">The cached <see cref="ILogger"/> resolved from the DI container, or
        /// <see langword="null"/> if unavailable.</param>
        /// <param name="host">The duplicate host entry.</param>
        [LoggerMessage(EventId = 56, Level = LogLevel.Warning,
            Message = "RabbitMQ:Hosts contains duplicate entry '{Host}'. Duplicates reduce effective failover capacity")]
        internal static partial void LogDuplicateHost(ILogger logger, string host);

        #endregion

        #region Logging -- Production Safety Warnings (57, 58)

        /// <summary>
        /// Logs a warning when a host entry is an IPv6 link-local address in a Production environment.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ValidateHostProductionSafety"/> -- emitted when the host parses as an
        /// <see cref="System.Net.IPAddress"/> with <see cref="System.Net.IPAddress.IsIPv6LinkLocal"/> set to
        /// <see langword="true"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because link-local addresses
        /// are not routable and unusual for production brokers, but not technically invalid.</para>
        /// </remarks>
        /// <param name="logger">The cached <see cref="ILogger"/> resolved from the DI container, or
        /// <see langword="null"/> if unavailable.</param>
        /// <param name="index">Zero-based index in the <see cref="Hosts"/> array.</param>
        /// <param name="host">The IPv6 link-local address string.</param>
        [LoggerMessage(EventId = 57, Level = LogLevel.Warning,
            Message = "RabbitMQ:Hosts[{Index}] ('{Host}') is an IPv6 link-local address (fe80::/10). " +
                      "Link-local addresses are not routable and are unusual for Production brokers")]
        internal static partial void LogIPv6LinkLocal(ILogger logger, int index, string host);

        /// <summary>
        /// Logs a warning when a host entry is in a private or reserved IPv4 range in a Production environment.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ValidateHostProductionSafety"/> -- emitted when the host parses as an IPv4
        /// address that <see cref="Utilities.Networking.IPUtilities.Classify(System.Net.IPAddress)"/> classifies as
        /// private or reserved.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because private ranges
        /// may indicate misconfiguration but could be intentional (VPN, VPC peering).</para>
        /// </remarks>
        /// <param name="logger">The cached <see cref="ILogger"/> resolved from the DI container, or
        /// <see langword="null"/> if unavailable.</param>
        /// <param name="index">Zero-based index in the <see cref="Hosts"/> array.</param>
        /// <param name="host">The private/reserved IP address string.</param>
        /// <param name="rangeDescription">Human-readable description of the matched range (e.g.,
        /// <c>"RFC 1918 private (10.0.0.0/8)"</c>).</param>
        [LoggerMessage(EventId = 58, Level = LogLevel.Warning,
            Message = "RabbitMQ:Hosts[{Index}] ('{Host}') is in the {RangeDescription} range. " +
                      "Production brokers typically use routable addresses. " +
                      "If this is intentional (e.g., VPN or VPC peering), this warning can be ignored")]
        internal static partial void LogPrivateIpRange(ILogger logger, int index, string host, string rangeDescription);

        #endregion

    }
}
