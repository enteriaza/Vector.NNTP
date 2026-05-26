// <copyright file="RabbitMqPublisherPool.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPublisherPool.Logging.cs -- Source-generated [LoggerMessage] partial methods for RabbitMqPublisherPool.
//
// Callers in RabbitMqPublisherPool.cs log debug scope creation after channel open.

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMqPublisherPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 1 -- reserved for <see cref="RabbitMqPublisherPool"/>.</para>
    /// </remarks>
    public sealed partial class RabbitMqPublisherPool
    {

        #region Logging -- Publisher Scopes (1)

        /// <summary>Logs debug scope creation after successful channel open.</summary>
        [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Created publisher scope.")]
        private partial void LogScopeCreated();

        #endregion

    }
}
