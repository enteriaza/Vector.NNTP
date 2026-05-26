// <copyright file="RabbitMqConsumerManager.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqConsumerManager.Logging.cs -- Source-generated [LoggerMessage] partial methods for RabbitMqConsumerManager.
//
// Callers in RabbitMqConsumerManager.cs log consumer subscription registration.

namespace MessageBus.Consuming
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMqConsumerManager"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 1 -- reserved for <see cref="RabbitMqConsumerManager"/>.</para>
    /// </remarks>
    public sealed partial class RabbitMqConsumerManager
    {

        #region Logging -- Subscriptions (1)

        /// <summary>Logs successful consumer registration.</summary>
        /// <param name="subscriptionId">Assigned subscription identifier.</param>
        /// <param name="queue">Consumed queue name.</param>
        [LoggerMessage(EventId = 1, Level = LogLevel.Information,
            Message = "Registered consumer subscription {SubscriptionId} on queue {Queue}.")]
        private partial void LogSubscriptionRegistered(Guid subscriptionId, string queue);

        #endregion

    }
}
