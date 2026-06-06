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
using Vector.NNTP.MessageBus.Metrics;
using Vector.NNTP.MessageBus.Publishing;
using Vector.NNTP.MessageBus.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Vector.NNTP.MessageBus.Consuming
{
    /// <summary>
    /// Dedicated long-lived consumer channels on <see cref="ConnectionPool"/> TCP connections.
    /// </summary>
    /// <remarks>
    /// <para><b>Layering:</b> Unlike <see cref="RabbitMqPublisherPool"/> (ephemeral channels per scope), this
    /// type keeps channels open for the process lifetime. It does not acquire
    /// <see cref="PublisherSlotLease"/> permits.</para>
    ///
    /// <para><b>Connection pick:</b> Selects the first <see cref="PooledConnection"/> with
    /// <see cref="PooledConnection.CanAcceptNewSlots"/>, otherwise the first snapshot entry, so consumer setup degrades
    /// gracefully when slots are saturated.</para>
    ///
    /// <para><b>Shutdown:</b> <see cref="IRabbitMqConsumerManager.StopAsync(CancellationToken)"/> sets
    /// <see cref="_stopped"/>, cancels all registrations, and clears the dictionary under <see cref="_gate"/>.</para>
    ///
    /// <para><b>Thread safety:</b> <see cref="_registrations"/> mutations are serialised; handlers must be non-blocking on
    /// the client I/O thread.</para>
    /// </remarks>
    /// <param name="pool">Connection pool providing <see cref="IConnection"/> instances.</param>
    /// <param name="logger">Logger for registration events; consumed by source-generated <c>[LoggerMessage]</c> methods.</param>
    /// <param name="metrics">Metrics sink for classified delivery failure counters.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pool"/>, <paramref name="logger"/>, or
    /// <paramref name="metrics"/> is <see langword="null"/>.</exception>
    internal sealed partial class RabbitMqConsumerManager(
        ConnectionPool pool,
        ILogger<RabbitMqConsumerManager> logger,
        MessageBusMetrics metrics) : IRabbitMqConsumerManager, IAsyncDisposable
    {
        /// <summary>Pooled TCP connections used to open consumer channels.</summary>
        private readonly ConnectionPool _pool = pool ?? throw new ArgumentNullException(nameof(pool));

        /// <summary>Active subscriptions keyed by opaque subscription id.</summary>
        private readonly Dictionary<Guid, ConsumerRegistration> _registrations = [];

        /// <summary>Serialises registration dictionary mutations.</summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>Metrics sink for classified delivery failures.</summary>
        private readonly MessageBusMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        /// <summary>When true, <see cref="IRabbitMqConsumerManager.RegisterSubscriptionAsync(string, AsyncEventHandler{BasicDeliverEventArgs}, CancellationToken)"/> rejects new work.</summary>
        private bool _stopped;

        /// <summary>
        /// Registers a consumer on a pooled connection and wraps the caller handler with delivery-boundary diagnostics.
        /// </summary>
        /// <param name="queue">Queue name to consume from.</param>
        /// <param name="handler">Caller delivery handler.</param>
        /// <param name="cancellationToken">Cancellation token for registration operations.</param>
        /// <returns>Subscription identifier for this registration.</returns>
        /// <exception cref="MessageBusUnavailableException">Thrown when the manager is stopped or no pooled connection exists.</exception>
        async Task<Guid> IRabbitMqConsumerManager.RegisterSubscriptionAsync(
            string queue,
            AsyncEventHandler<BasicDeliverEventArgs> handler,
            CancellationToken cancellationToken)
        {
            if (_stopped)
                throw new MessageBusUnavailableException("Consumer manager is stopped.");
            Guid subscriptionId = Guid.NewGuid();
            PooledConnection connection = SelectConnection()
                ?? throw new MessageBusUnavailableException("No pooled TCP connection available for consumer registration.");
            IChannel channel = await connection.Connection.CreateChannelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += WrapHandler(subscriptionId, handler);
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
        async Task IRabbitMqConsumerManager.StopAsync(CancellationToken cancellationToken)
        {
            _stopped = true;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                while (_registrations.Count > 0)
                {
                    using IEnumerator<KeyValuePair<Guid, ConsumerRegistration>> enumerator = _registrations.GetEnumerator();
                    _ = enumerator.MoveNext();
                    KeyValuePair<Guid, ConsumerRegistration> pair = enumerator.Current;
                    _ = _registrations.Remove(pair.Key);
                    await pair.Value.DisposeAsync().ConfigureAwait(false);
                }
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
            await ((IRabbitMqConsumerManager)this).StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Selects a pooled connection for consumer channel creation without using LINQ allocations.
        /// </summary>
        /// <returns>The selected pooled connection, or <see langword="null"/> when the snapshot is empty.</returns>
        private PooledConnection? SelectConnection()
        {
            IReadOnlyList<PooledConnection> snapshot = _pool.Snapshot;
            PooledConnection? fallback = null;
            for (int i = 0; i < snapshot.Count; i++)
            {
                PooledConnection candidate = snapshot[i];
                fallback ??= candidate;
                if (candidate.CanAcceptNewSlots)
                    return candidate;
            }
            return fallback;
        }

        /// <summary>
        /// Wraps caller handlers with correlation extraction, delivery logging, and classified failure telemetry.
        /// </summary>
        /// <param name="subscriptionId">Subscription identifier for diagnostics.</param>
        /// <param name="handler">Caller delivery handler to invoke.</param>
        /// <returns>Wrapped handler delegate registered on the RabbitMQ consumer.</returns>
        private AsyncEventHandler<BasicDeliverEventArgs> WrapHandler(Guid subscriptionId, AsyncEventHandler<BasicDeliverEventArgs> handler)
        {
            return async (sender, args) =>
            {
                string? correlationId = TryExtractCorrelationId(args);
                if (logger.IsEnabled(LogLevel.Debug))
                    LogMessageDelivered(subscriptionId, args.DeliveryTag, correlationId);
                using System.Diagnostics.Activity? activity = MessageBusTelemetry.ActivitySource.StartActivity(
                    "messagebus.consume",
                    System.Diagnostics.ActivityKind.Consumer);
                _ = (activity?.SetTag("messaging.system", "rabbitmq"));
                _ = (activity?.SetTag("messaging.destination.name", args.RoutingKey));
                _ = (activity?.SetTag("messaging.message.id", args.DeliveryTag));
                _ = (activity?.SetTag("messagebus.subscription_id", subscriptionId));
                if (!string.IsNullOrWhiteSpace(correlationId))
                    _ = (activity?.SetTag("messagebus.correlation_id", correlationId));

                try
                {
                    await handler(sender, args).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    string failureClass = MessageBusFailureClassifier.Classify(ex);
                    _metrics.RecordDeliveryFailure(failureClass);
                    LogHandlerFailure(subscriptionId, failureClass, correlationId);
                    throw;
                }
            };
        }

        /// <summary>
        /// Extracts a correlation id from AMQP headers when present.
        /// </summary>
        /// <param name="args">Delivery event arguments carrying message properties.</param>
        /// <returns>Correlation id string, or <see langword="null"/> when absent.</returns>
        private static string? TryExtractCorrelationId(BasicDeliverEventArgs args)
        {
            IDictionary<string, object?>? headers = args.BasicProperties?.Headers;
            return headers is null || !headers.TryGetValue(MessageBusCorrelationHeaders.CorrelationIdHeaderName, out object? value)
                ? null
                : value switch
                {
                    string text when !string.IsNullOrWhiteSpace(text) => text,
                    byte[] bytes when bytes.Length > 0 => System.Text.Encoding.UTF8.GetString(bytes),
                    _ => null,
                };
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

            /// <summary>
            /// Cancels and disposes the underlying consumer channel.
            /// </summary>
            /// <returns>A value task that completes when cancellation and disposal finish.</returns>
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

