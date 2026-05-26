// <copyright file="RabbitMqPoolFlowControlMonitor.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPoolFlowControlMonitor.Logging.cs -- Source-generated [LoggerMessage] partial methods for
// RabbitMqPoolFlowControlMonitor.
//
// Callers in RabbitMqPoolFlowControlMonitor.cs log stalled quarantine and scan-loop failures.

namespace MessageBus.Connections
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for
    /// <see cref="RabbitMqPoolFlowControlMonitor"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 1--2 -- reserved for <see cref="RabbitMqPoolFlowControlMonitor"/>.</para>
    /// </remarks>
    public sealed partial class RabbitMqPoolFlowControlMonitor
    {

        #region Logging -- Flow Control (1-2)

        /// <summary>Logs when one or more connections are quarantined as stalled.</summary>
        /// <param name="stalledCount">Number of connections newly marked <see cref="PooledConnection.IsStalled"/>.</param>
        /// <param name="blockedTimeout">Configured blocked timeout that triggered quarantine.</param>
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
            Message = "Quarantined {StalledCount} pooled connection(s) after remaining blocked longer than {BlockedTimeout}.")]
        private partial void LogConnectionsStalled(int stalledCount, TimeSpan blockedTimeout);

        /// <summary>Logs an unexpected exception during a flow-control scan iteration.</summary>
        /// <param name="ex">Exception thrown by the scan loop.</param>
        [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Flow-control monitor scan failed.")]
        private partial void LogFlowControlScanError(Exception ex);

        #endregion

    }
}
