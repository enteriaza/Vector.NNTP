// <copyright file="RabbitMqPublisherScope.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPublisherScope.Logging.cs -- Source-generated [LoggerMessage] declarations for publisher scope failures.
//
// EventId range allocation:
//   publish: 300-309.

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> methods for <see cref="RabbitMqPublisherScope"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Event ID range:</b> Publish failures use 300–309 (see file synopsis).</para>
    /// <para><b>Logging:</b> Events capture per-scope publish faults with bounded classifier labels for production triage.</para>
    /// </remarks>
    internal sealed partial class RabbitMqPublisherScope
    {
        /// <summary>
        /// Logs a classified publish failure emitted from a publisher scope.
        /// </summary>
        /// <param name="scopeId">Publisher scope identifier.</param>
        /// <param name="failureClass">Bounded failure class label.</param>
        /// <param name="exchange">Target exchange.</param>
        /// <param name="routingKey">Message routing key.</param>
        /// <param name="correlationId">Optional message correlation identifier.</param>
        [LoggerMessage(
            EventId = 302,
            Level = LogLevel.Warning,
            Message = "Publisher scope {ScopeId} publish failed (class={FailureClass}) exchange={Exchange} routingKey={RoutingKey} correlationId={CorrelationId}.")]
        private partial void LogPublishFailed(Guid scopeId, string failureClass, string exchange, string routingKey, string? correlationId);
    }
}
