// <copyright file="IRabbitMqConsumerManager.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// IRabbitMqConsumerManager.cs -- Contract for long-lived AMQP consumer channels on pooled TCP connections.
//
// Unlike publisher scopes (ephemeral channels per RPC), consumers remain open for the process lifetime and must be
// rebound after TCP faults. Hosts register queue handlers once at startup.
//
// Thread safety:
//   Implementations serialise registration mutations; delivery handlers run on the RabbitMQ client I/O thread.

using RabbitMQ.Client.Events;

namespace Vector.NNTP.MessageBus.Consuming
{
    /// <summary>
    /// Manages long-lived consumer <see cref="RabbitMQ.Client.IChannel"/> instances on pooled TCP connections.
    /// </summary>
    /// <remarks>
    /// <para><b>Layering:</b> Acquires a <see cref="Connections.PooledConnection"/> from
    /// <see cref="Connections.ConnectionPool"/> but does not participate in publisher slot accounting — consumer channels
    /// are orthogonal to <see cref="Connections.PublisherSlotLease"/>.</para>
    ///
    /// <para><b>Delivery semantics:</b> Registrations use <c>autoAck: false</c>; handlers must ack/nack explicitly per
    /// RabbitMQ.Client 7 async consumer APIs.</para>
    ///
    /// <para><b>Lifecycle:</b> <see cref="StopAsync"/> cancels all subscriptions and closes channels during host shutdown.</para>
    /// </remarks>
    public interface IRabbitMqConsumerManager
    {
        /// <summary>
        /// Registers a subscription with a dedicated long-lived channel on an available pooled connection.
        /// </summary>
        /// <param name="queue">AMQP queue name to consume.</param>
        /// <param name="handler">Async delivery handler invoked on the client library I/O thread.</param>
        /// <param name="cancellationToken">Cancellation token for channel creation and registration.</param>
        /// <returns>Opaque subscription identifier for correlation across future reconnect enhancements.</returns>
        /// <remarks>
        /// <para><b>Connection selection:</b> Prefers a connection that can accept new slots; falls back to any snapshot
        /// entry when all connections are saturated.</para>
        /// </remarks>
        /// <exception cref="Exceptions.MessageBusUnavailableException">
        /// Thrown when the manager is stopped or no pooled TCP connection is available.
        /// </exception>
        public Task<Guid> RegisterSubscriptionAsync(
            string queue,
            AsyncEventHandler<BasicDeliverEventArgs> handler,
            CancellationToken cancellationToken);

        /// <summary>
        /// Stops accepting new subscriptions and disposes all active consumer channels.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown cancellation token.</param>
        /// <returns>A task representing the asynchronous stop operation.</returns>
        public Task StopAsync(CancellationToken cancellationToken);
    }
}

