// <copyright file="RabbitMqPoolSupervisor.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPoolSupervisor.Logging.cs -- Source-generated [LoggerMessage] partial methods for RabbitMqPoolSupervisor.
//
// Centralises supervisor lifecycle log events per CONTRIBUTING.md.  Callers in RabbitMqPoolSupervisor.cs invoke
// LogSupervisorStarted after the pool reaches MinConnections and LogShutdownDrainTimeout when pool disposal exceeds
// MaximumShutdownDrainTimeout.
//
// Thread safety:
//   Source-generated methods use the DI-injected _logger field; ILogger is thread-safe by contract.
//
// Cross-platform:
//   Fully portable on .NET 8 (Windows x64, Linux x64).  No OS-specific APIs.

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMqPoolSupervisor"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 1--2 -- reserved for <see cref="RabbitMqPoolSupervisor"/>.</para>
    ///
    /// <para><b>Pattern:</b> Each method is a <see langword="private"/> <see langword="partial"/> method annotated with
    /// <see cref="LoggerMessageAttribute"/>.  The source generator emits the implementation at compile time using the
    /// <c>_logger</c> field from the primary partial file.</para>
    ///
    /// <para><b>ASCII compliance:</b> All <c>Message</c> strings contain only ASCII characters per CONTRIBUTING.md.</para>
    /// </remarks>
    public sealed partial class RabbitMqPoolSupervisor
    {

        #region Logging -- Supervisor Lifecycle (1-2)

        /// <summary>
        /// Logs supervisor startup after the pool reaches minimum connections.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="StartAsync"/> -- after <see cref="ConnectionPool.StartAsync"/> completes and
        /// initial health is published.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> -- startup banner per CONTRIBUTING.md.</para>
        /// </remarks>
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "RabbitMQ pool supervisor started.")]
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
        [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
            Message = "Pool shutdown exceeded MaximumShutdownDrainTimeout.")]
        private partial void LogShutdownDrainTimeout();

        #endregion

    }
}
