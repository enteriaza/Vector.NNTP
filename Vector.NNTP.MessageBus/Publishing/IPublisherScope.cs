// <copyright file="IPublisherScope.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// IPublisherScope.cs -- One AMQP channel and one publisher-confirm lifecycle per host RPC transaction.
//
// Hosts obtain scopes from IRabbitMqPublisherPool, publish one or more messages serially, then await DisposeAsync to
// close the channel and release the underlying ConnectionPool slot permit.
//
// Thread safety:
//   Implementations are not thread-safe; serialize PublishAsync within a scope.

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// One AMQP <see cref="RabbitMQ.Client.IChannel"/> and one publisher-confirm lifecycle per RPC transaction.
    /// </summary>
    /// <remarks>
    /// <para><b>Lifecycle:</b> Created by <see cref="IRabbitMqPublisherPool.CreateScopeAsync"/>; disposed to close the
    /// channel and release the <see cref="Connections.PublisherSlotLease"/>.</para>
    ///
    /// <para><b>Thread safety:</b> Not thread-safe — callers must serialize <see cref="PublishAsync"/> invocations on a
    /// single scope instance.</para>
    ///
    /// <para><b>Delivery semantics:</b> At-least-once — hosts must tolerate duplicate messages when retrying failed RPCs
    /// after a successful broker accept.</para>
    ///
    /// <para><b>Confirms:</b> Implementations await broker publisher confirmation as part of
    /// <see cref="PublishAsync"/> (RabbitMQ.Client 7 model).</para>
    /// </remarks>
    public interface IPublisherScope : IAsyncDisposable
    {
        /// <summary>
        /// Publishes a message on the scope channel and awaits publisher confirmation.
        /// </summary>
        /// <param name="exchange">Target exchange name.</param>
        /// <param name="routingKey">AMQP routing key.</param>
        /// <param name="body">Message body bytes.</param>
        /// <param name="cancellationToken">Caller cancellation token (distinct from confirm timeout).</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the broker acknowledges the publish.</returns>
        /// <remarks>
        /// <para><b>Timeout:</b> Implementations link <paramref name="cancellationToken"/> with
        /// <see cref="Configuration.RabbitMQOptions.PublishConfirmTimeout"/>; caller cancellation must not be reported as
        /// a confirm timeout.</para>
        /// </remarks>
        /// <exception cref="Exceptions.MessageBusPublishConfirmTimeoutException">
        /// Thrown when confirmation is not received before the configured confirm timeout and the caller did not cancel.
        /// </exception>
        /// <exception cref="ObjectDisposedException">Thrown when the scope was already disposed.</exception>
        public ValueTask PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, CancellationToken cancellationToken);
    }
}

