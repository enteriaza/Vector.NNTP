// <copyright file="IRabbitMqPublisherPool.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// IRabbitMqPublisherPool.cs -- Factory contract for ephemeral publisher scopes backed by pooled TCP slots.
//
// Each CreateScopeAsync acquires one publisher slot permit and opens one AMQP channel with publisher confirms enabled.
// Host RPC layers should create one scope per transaction and dispose it promptly to release slots.

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// Acquires <see cref="IPublisherScope"/> instances backed by ephemeral AMQP channels on pooled TCP connections.
    /// </summary>
    /// <remarks>
    /// <para><b>Layering:</b> Delegates slot acquisition to <see cref="Connections.ConnectionPool"/> and channel creation to
    /// <see cref="RabbitMqPublisherPool"/>. Callers interact only with <see cref="IPublisherScope"/> for publish/dispose.</para>
    ///
    /// <para><b>At-least-once:</b> Scopes do not retry publishes; hosts must tolerate duplicate delivery on retry.</para>
    ///
    /// <para><b>Thread safety:</b> Implementations are safe for concurrent <see cref="CreateScopeAsync"/> calls; each returned
    /// scope is not thread-safe.</para>
    /// </remarks>
    public interface IRabbitMqPublisherPool
    {
        /// <summary>
        /// Creates a publisher scope (one slot permit, one ephemeral channel, publisher confirms enabled).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for slot wait and channel creation.</param>
        /// <returns>Disposable scope that releases the slot when disposed.</returns>
        /// <remarks>
        /// <para><b>Failure path:</b> When channel creation fails after slot acquisition, implementations must release the
        /// slot before propagating the fault.</para>
        /// </remarks>
        /// <exception cref="Exceptions.MessageBusUnavailableException">
        /// Thrown when the pool rejects new slots or waiter limits are exceeded.
        /// </exception>
        /// <exception cref="Exceptions.MessageBusLeaseTimeoutException">
        /// Thrown when no slot becomes available before <see cref="Configuration.RabbitMQOptions.ChannelLeaseTimeout"/>.
        /// </exception>
        /// <exception cref="Exceptions.MessageBusConnectionFaultException">
        /// Thrown when AMQP channel creation fails after a slot was acquired.
        /// </exception>
        public Task<IPublisherScope> CreateScopeAsync(CancellationToken cancellationToken);
    }
}

