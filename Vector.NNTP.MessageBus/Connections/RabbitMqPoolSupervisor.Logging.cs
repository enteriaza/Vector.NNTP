// <copyright file="RabbitMqPoolSupervisor.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPoolSupervisor.Logging.cs -- Source-generated [LoggerMessage] partial methods for RabbitMqPoolSupervisor.
//
// Centralises supervisor lifecycle log events per CONTRIBUTING.md.  Callers in RabbitMqPoolSupervisor.cs invoke
// LogMessageBusInitialized and LogSupervisorStarted after the pool reaches MinConnections, and LogShutdownDrainTimeout
// when pool disposal exceeds MaximumShutdownDrainTimeout.
//
// EventId range allocation:
//   supervisor startup: 105
//   supervisor operational: 500-509.
//
// Thread safety:
//   Source-generated methods use the primary-constructor logger parameter; ILogger is thread-safe by contract.
//
// Cross-platform:
//   Fully portable on .NET 8 (Windows x64, Linux x64).  No OS-specific APIs.

using Vector.NNTP.MessageBus.Configuration;

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMqPoolSupervisor"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 105 and 501-502 -- reserved for <see cref="RabbitMqPoolSupervisor"/>.</para>
    ///
    /// <para><b>Pattern:</b> Each method is a <see langword="private"/> <see langword="partial"/> method annotated with
    /// <see cref="LoggerMessageAttribute"/>.  The source generator emits the implementation at compile time using the
    /// <c>logger</c> primary-constructor parameter from the primary partial file.</para>
    ///
    /// <para><b>ASCII compliance:</b> All <c>Message</c> strings contain only ASCII characters per CONTRIBUTING.md.</para>
    /// </remarks>
    internal sealed partial class RabbitMqPoolSupervisor
    {

        #region Logging -- Supervisor Lifecycle (105, 501-502)

        /// <summary>
        /// Logs MessageBus pool initialization details after startup succeeds.
        /// </summary>
        /// <param name="brokerCount">Configured RabbitMQ broker endpoint count.</param>
        /// <param name="minConnections">Configured minimum connection count.</param>
        /// <param name="maxConnections">Configured maximum connection count.</param>
        /// <param name="tlsEnabled">Whether TLS is enabled for AMQP connections.</param>
        /// <param name="publisherConfirmsEnabled">Whether publisher confirmations are enabled by design.</param>
        [LoggerMessage(
            EventId = 105,
            Level = LogLevel.Information,
            Message = "MessageBus: MessageBus initialized -- BrokerCount={BrokerCount} MinConnections={MinConnections} MaxConnections={MaxConnections} Tls={TlsEnabled} PublisherConfirms={PublisherConfirmsEnabled}")]
        private partial void LogMessageBusInitialized(
            int brokerCount,
            int minConnections,
            int maxConnections,
            bool tlsEnabled,
            bool publisherConfirmsEnabled);

        /// <summary>
        /// Logs supervisor startup after the pool reaches minimum connections.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="StartAsync"/> -- after <see cref="ConnectionPool.StartAsync"/> completes and
        /// initial health is published.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> -- startup banner per CONTRIBUTING.md.</para>
        /// </remarks>
        [LoggerMessage(EventId = 501, Level = LogLevel.Information, Message = "RabbitMQ pool supervisor started.")]
        private partial void LogSupervisorStarted();

        /// <summary>
        /// Logs when pool disposal exceeds the configured shutdown drain cap.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="StopAsync"/> -- when <see cref="ConnectionPool.DisposeAsync"/> is cancelled by
        /// <see cref="RabbitMQOptions.MaximumShutdownDrainTimeout"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> -- shutdown did not complete within the drain
        /// budget; operators may need to investigate stuck publisher scopes or broker back-pressure.</para>
        /// </remarks>
        [LoggerMessage(EventId = 502, Level = LogLevel.Warning,
            Message = "Pool shutdown exceeded MaximumShutdownDrainTimeout.")]
        private partial void LogShutdownDrainTimeout();

        #endregion

    }
}
