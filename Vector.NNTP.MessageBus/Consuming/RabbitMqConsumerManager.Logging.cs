// <copyright file="RabbitMqConsumerManager.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqConsumerManager.Logging.cs -- Source-generated [LoggerMessage] partial methods for RabbitMqConsumerManager.
//
// Callers in RabbitMqConsumerManager.cs log registration and per-delivery boundaries.
//
// EventId range allocation:
//   consume: 400-409.

namespace Vector.NNTP.MessageBus.Consuming
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RabbitMqConsumerManager"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> 400-402 -- reserved for consumer manager lifecycle and deliveries.</para>
    /// </remarks>
    internal sealed partial class RabbitMqConsumerManager
    {

        #region Logging -- Subscriptions and Deliveries (400-402)

        /// <summary>Logs successful consumer registration.</summary>
        /// <param name="subscriptionId">Assigned subscription identifier.</param>
        /// <param name="queue">Consumed queue name.</param>
        [LoggerMessage(EventId = 400, Level = LogLevel.Information,
            Message = "Registered consumer subscription {SubscriptionId} on queue {Queue}.")]
        private partial void LogSubscriptionRegistered(Guid subscriptionId, string queue);

        /// <summary>Logs a delivered message before invoking user handler.</summary>
        /// <param name="subscriptionId">Registered subscription identifier.</param>
        /// <param name="deliveryTag">Broker delivery tag.</param>
        /// <param name="correlationId">Extracted correlation id header value, when present.</param>
        [LoggerMessage(EventId = 401, Level = LogLevel.Debug,
            Message = "Delivered message to subscription {SubscriptionId} deliveryTag={DeliveryTag} correlationId={CorrelationId}.")]
        private partial void LogMessageDelivered(Guid subscriptionId, ulong deliveryTag, string? correlationId);

        /// <summary>Logs when a subscription handler throws during delivery processing.</summary>
        /// <param name="subscriptionId">Registered subscription identifier.</param>
        /// <param name="failureClass">Bounded failure classifier label.</param>
        /// <param name="correlationId">Extracted correlation id header value, when present.</param>
        [LoggerMessage(EventId = 402, Level = LogLevel.Warning,
            Message = "Consumer handler failed for subscription {SubscriptionId} (class={FailureClass}) correlationId={CorrelationId}.")]
        private partial void LogHandlerFailure(Guid subscriptionId, string failureClass, string? correlationId);

        #endregion

    }
}
