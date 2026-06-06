// <copyright file="RabbitMqPublisherPool.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPublisherPool.Logging.cs -- Source-generated [LoggerMessage] partial methods for RabbitMqPublisherPool.
//
// Callers in RabbitMqPublisherPool.cs log scope creation and channel faults.
//
// EventId range allocation:
//   publish: 300-309.

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMqPublisherPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 300-301 -- reserved for pool-level publisher events.</para>
    /// </remarks>
    internal sealed partial class RabbitMqPublisherPool
    {

        #region Logging -- Publisher Scopes (300-301)

        /// <summary>
        /// Logs debug scope creation after successful channel open.
        /// </summary>
        /// <param name="scopeId">Assigned publisher scope identifier.</param>
        [LoggerMessage(EventId = 300, Level = LogLevel.Debug, Message = "Created publisher scope {ScopeId}.")]
        private partial void LogScopeCreated(Guid scopeId);

        /// <summary>
        /// Logs a classified fault when channel creation fails for a leased slot.
        /// </summary>
        /// <param name="failureClass">Bounded failure class label.</param>
        [LoggerMessage(EventId = 301, Level = LogLevel.Error, Message = "Failed to create publisher channel (class={FailureClass}).")]
        private partial void LogChannelFault(string failureClass);

        #endregion

    }
}
