// <copyright file="MessageBusPublishConfirmTimeoutException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusPublishConfirmTimeoutException.cs -- Broker did not acknowledge a publish within PublishConfirmTimeout.
//
// Raised only when caller cancellation did not trigger the timeout. Indicates broker back-pressure, network loss, or
// confirm pipeline stall — not client-side OperationCanceledException from the host token.

namespace MessageBus.Exceptions
{
    /// <summary>
    /// Thrown when publisher confirmation is not received within
    /// <see cref="Configuration.RabbitMQOptions.PublishConfirmTimeout"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Distinct from cancellation:</b> <see cref="Publishing.RabbitMqPublisherScope"/> throws this type only when
    /// the linked timeout fires and the caller's <see cref="CancellationToken"/> was not cancelled.</para>
    ///
    /// <para><b>Recovery:</b> At-least-once semantics apply — the message may have reached the broker. Hosts must use
    /// idempotent handlers or deduplication before retrying the RPC.</para>
    /// </remarks>
    /// <remarks>Initializes a new instance of the <see cref="MessageBusPublishConfirmTimeoutException"/> class.</remarks>
    /// <param name="message">Human-readable timeout context including configured duration when helpful.</param>
    public sealed class MessageBusPublishConfirmTimeoutException(string message) : MessageBusException(message)
    {
    }
}

