// <copyright file="RabbitMqConsumerManager.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqConsumerManager.cs -- Long-lived AMQP consumer channels registered on pooled TCP connections.
//
// Hosts call RegisterSubscriptionAsync once per queue. Each subscription owns a dedicated IChannel and consumer tag
// until StopAsync or DisposeAsync runs during shutdown. Publisher slot leases are not consumed for consumers.
//
// Thread safety:
//   Registration dictionary mutations are serialised with SemaphoreSlim; delivery handlers run on the RabbitMQ
//   client I/O thread and must not block.
//
// Cross-platform:
//   Portable BCL + RabbitMQ.Client; Windows x64 and Linux x64 on .NET 8.
//
// Logging: [LoggerMessage] partial methods in RabbitMqConsumerManager.Logging.cs.

using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Exceptions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Vector.NNTP.MessageBus.Consuming
{
    /// <summary>
    /// Dedicated long-lived consumer channels on <see cref="ConnectionPool"/> TCP connections.
    /// </summary>
    /// <remarks>
    /// <para><b>Layering:</b> Unlike <see cref="Publishing.RabbitMqPublisherPool"/> (ephemeral channels per scope), this
    /// type keeps channels open for the process lifetime. It does not acquire
    /// <see cref="PublisherSlotLease"/> permits.</para>
    ///
    /// <para><b>Connection pick:</b> Selects the first <see cref="PooledConnection"/> with
    /// <see cref="PooledConnection.CanAcceptNewSlots"/>, otherwise the first snapshot entry, so consumer setup degrades
    /// gracefully when slots are saturated.</para>
    ///
    /// <para><b>Shutdown:</b> <see cref="StopAsync"/> sets <see cref="_stopped"/>, cancels all registrations, and clears
    /// the dictionary under <see cref="_gate"/>.</para>
    ///
    /// <para><b>Thread safety:</b> <see cref="_registrations"/> mutations are serialised; handlers must be non-blocking on
    /// the client I/O thread.</para>
    /// </remarks>
    /// <param name="pool">Connection pool providing <see cref="IConnection"/> instances.</param>
    /// <param name="logger">Logger for registration events; consumed by source-generated <c>[LoggerMessage]</c> methods.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pool"/> is <see langword="null"/>.</exception>
    public sealed partial class RabbitMqConsumerManager(
        ConnectionPool pool,
        ILogger<RabbitMqConsumerManager> logger) : IRabbitMqConsumerManager, IAsyncDisposable
    {
        /// <summary>Pooled TCP connections used to open consumer channels.</summary>
        private readonly ConnectionPool _pool = pool ?? throw new ArgumentNullException(nameof(pool));

        /// <summary>Active subscriptions keyed by opaque subscription id.</summary>
        private readonly Dictionary<Guid, ConsumerRegistration> _registrations = [];

        /// <summary>Serialises registration dictionary mutations.</summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>When true, <see cref="RegisterSubscriptionAsync"/> rejects new work.</summary>
        private bool _stopped;

        /// <inheritdoc />
        /// <exception cref="MessageBusUnavailableException">
        /// Thrown when <see cref="_stopped"/> is true or no connection exists in <see cref="ConnectionPool.Snapshot"/>.
        /// </exception>
        public async Task<Guid> RegisterSubscriptionAsync(
            string queue,
            AsyncEventHandler<BasicDeliverEventArgs> handler,
            CancellationToken cancellationToken)
        {
            if (_stopped)
                throw new MessageBusUnavailableException("Consumer manager is stopped.");
            Guid subscriptionId = Guid.NewGuid();
            PooledConnection? connection = (_pool.Snapshot.FirstOrDefault(c => c.CanAcceptNewSlots)
                ?? _pool.Snapshot.FirstOrDefault()) ?? throw new MessageBusUnavailableException("No pooled TCP connection available for consumer registration.");
            IChannel channel = await connection.Connection.CreateChannelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += handler;
            string consumerTag = await channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: consumer, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            ConsumerRegistration registration = new(channel, consumerTag);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _registrations[subscriptionId] = registration;
            }
            finally
            {
                _ = _gate.Release();
            }
            LogSubscriptionRegistered(subscriptionId, queue);
            return subscriptionId;
        }

        /// <summary>
        /// Stops the consumer manager.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the consumer manager is stopped.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the cancellation token is canceled.</exception>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _stopped = true;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (ConsumerRegistration registration in _registrations.Values.ToArray())
                    await registration.DisposeAsync().ConfigureAwait(false);
                _registrations.Clear();
            }
            finally
            {
                _ = _gate.Release();
            }
        }

        /// <summary>
        /// Disposes the consumer manager.
        /// </summary>
        /// <returns>A task that completes when the consumer manager is disposed.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the cancellation token is canceled.</exception>
        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Owns one consumer channel and cancels the consumer tag on disposal.
        /// </summary>
        private sealed class ConsumerRegistration : IAsyncDisposable
        {
            /// <summary>AMQP channel hosting the consumer.</summary>
            private readonly IChannel _channel;

            /// <summary>Tag returned by <see cref="IChannel.BasicConsumeAsync"/> used for cancellation.</summary>
            private readonly string _consumerTag;

            /// <summary>Initializes registration state for one subscription.</summary>
            /// <param name="channel">Open consumer channel.</param>
            /// <param name="consumerTag">Broker consumer tag.</param>
            internal ConsumerRegistration(IChannel channel, string consumerTag)
            {
                _channel = channel;
                _consumerTag = consumerTag;
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (_channel.IsOpen)
                {
                    await _channel.BasicCancelAsync(_consumerTag).ConfigureAwait(false);
                    await _channel.CloseAsync().ConfigureAwait(false);
                }
                await _channel.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

