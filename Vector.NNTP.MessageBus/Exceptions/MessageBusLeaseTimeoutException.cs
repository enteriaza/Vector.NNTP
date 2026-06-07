// <copyright file="MessageBusLeaseTimeoutException.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusLeaseTimeoutException.cs -- Publisher slot acquisition exceeded ChannelLeaseTimeout.
//
// Indicates pool contention or insufficient ChannelPoolSize/MinConnections for offered load. Distinct from broker
// unavailability when the pool is healthy but fully subscribed.

namespace Vector.NNTP.MessageBus.Exceptions
{
    /// <summary>
    /// Thrown when <see cref="Connections.ConnectionPool.AcquirePublisherSlotAsync"/> exceeds
    /// <see cref="Configuration.RabbitMQOptions.ChannelLeaseTimeout"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Recovery:</b> Treat as overload — retry with jitter, scale pool settings, or shed load. Not a substitute for
    /// <see cref="MessageBusUnavailableException"/> when the pool is administratively closed.</para>
    ///
    /// <para><b>Tuning:</b> Review <see cref="Configuration.RabbitMQOptions.ChannelPoolSize"/>,
    /// <see cref="Configuration.RabbitMQOptions.MinConnections"/>, and
    /// <see cref="Configuration.RabbitMQOptions.MaxPendingLeaseWaiters"/> when this appears under steady-state load.</para>
    /// </remarks>
    /// <param name="message">Human-readable failure context for logs and metrics.</param>
    public sealed class MessageBusLeaseTimeoutException(string message) : MessageBusException(message)
    {
    }
}

