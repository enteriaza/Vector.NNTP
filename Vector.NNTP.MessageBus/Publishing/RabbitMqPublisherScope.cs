// <copyright file="RabbitMqPublisherScope.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPublisherScope.cs -- Ephemeral-channel publisher scope with per-publish confirmation tracking.
//
// Wraps one IChannel opened by RabbitMqPublisherPool and one PublisherSlotLease. PublishAsync awaits broker confirms;
// DisposeAsync closes the channel and releases the slot. Not thread-safe.
//
// Allocation:
//   Reuses a process-wide BasicProperties instance for publishes; one linked CancellationTokenSource per publish.
//
// Cross-platform:
//   Portable BCL + RabbitMQ.Client 7; Windows x64 and Linux x64 on .NET 8.

using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Exceptions;
using Vector.NNTP.MessageBus.Metrics;
using RabbitMQ.Client;

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// Ephemeral-channel <see cref="IPublisherScope"/> with per-publish confirmation tracking.
    /// </summary>
    /// <remarks>
    /// <para><b>Confirms:</b> RabbitMQ.Client 7 completes publisher confirms when
    /// <see cref="IChannel.BasicPublishAsync"/> is awaited. <see cref="Configuration.RabbitMQOptions.PublishConfirmTimeout"/>
    /// bounds the wait via a linked <see cref="CancellationTokenSource"/>.</para>
    ///
    /// <para><b>Thread safety:</b> Not thread-safe — serialize <see cref="PublishAsync"/> within one scope.</para>
    ///
    /// <para><b>Dispose order:</b> Closes the channel (when open), disposes the channel, then disposes
    /// <see cref="PublisherSlotLease"/> in a <c>finally</c> block so slots are not leaked on close failures.</para>
    ///
    /// <para><b>Allocation:</b> <see cref="PublishProperties"/> is shared read-only state; each publish may allocate a linked
    /// CTS when timeouts are enabled.</para>
    /// </remarks>
    internal sealed class RabbitMqPublisherScope : IPublisherScope
    {
        /// <summary>
        /// Shared empty <see cref="BasicProperties"/> for fire-and-forget publishes without per-message headers.
        /// </summary>
        /// <remarks>Read-only after construction; safe to reuse across concurrent scopes because each scope serialises
        /// publishes.</remarks>
        private static readonly BasicProperties PublishProperties = new();

        /// <summary>Slot lease released in <see cref="DisposeAsync"/>.</summary>
        private readonly PublisherSlotLease _slotLease;

        /// <summary>Ephemeral AMQP channel for this scope.</summary>
        private readonly IChannel _channel;

        /// <summary>Upper bound for publisher confirm wait per publish.</summary>
        private readonly TimeSpan _confirmTimeout;

        /// <summary>Non-zero after <see cref="DisposeAsync"/> has entered disposal.</summary>
        private int _disposed;

        /// <summary>Initializes a new instance of the <see cref="RabbitMqPublisherScope"/> class.</summary>
        /// <param name="slotLease">Owning slot lease; disposed after the channel.</param>
        /// <param name="channel">Open ephemeral channel with confirms enabled.</param>
        /// <param name="confirmTimeout">Per-publish confirm wait cap.</param>
        internal RabbitMqPublisherScope(PublisherSlotLease slotLease, IChannel channel, TimeSpan confirmTimeout)
        {
            _slotLease = slotLease;
            _channel = channel;
            _confirmTimeout = confirmTimeout;
        }

        /// <inheritdoc />
        /// <exception cref="ObjectDisposedException">Thrown when the scope was already disposed.</exception>
        /// <exception cref="MessageBusPublishConfirmTimeoutException">
        /// Thrown when confirm wait exceeds <see cref="_confirmTimeout"/> and <paramref name="cancellationToken"/> was not
        /// cancelled.
        /// </exception>
        public async ValueTask PublishAsync(
            string exchange,
            string routingKey,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_confirmTimeout);
            try
            {
                await _channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: PublishProperties,
                    body: body,
                    cancellationToken: timeoutSource.Token).ConfigureAwait(false);
                MessageBusMeters.RecordPublish();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                MessageBusMeters.RecordConfirmTimeout();
                throw new MessageBusPublishConfirmTimeoutException(
                    $"Publisher confirm was not received within {_confirmTimeout}.");
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            {
                if (_channel.IsOpen)
                    await _channel.CloseAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
                await _channel.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await _slotLease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

