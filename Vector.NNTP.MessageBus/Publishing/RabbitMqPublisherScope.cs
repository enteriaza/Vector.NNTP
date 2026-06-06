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
using Vector.NNTP.MessageBus.Telemetry;
using RabbitMQ.Client;
using System.Diagnostics;

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// Ephemeral-channel <see cref="IPublisherScope"/> with per-publish confirmation tracking.
    /// </summary>
    /// <remarks>
    /// <para><b>Confirms:</b> RabbitMQ.Client 7 completes publisher confirms when
    /// <c>IChannel.BasicPublishAsync</c> is awaited. <see cref="Configuration.RabbitMQOptions.PublishConfirmTimeout"/>
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
    internal sealed partial class RabbitMqPublisherScope : IPublisherScope
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

        /// <summary>Per-process metrics recorder.</summary>
        private readonly MessageBusMetrics _metrics;

        /// <summary>Logger used for classified publish failures.</summary>
        private readonly ILogger<RabbitMqPublisherScope> _logger;

        /// <summary>Non-zero after <see cref="DisposeAsync"/> has entered disposal.</summary>
        private int _disposed;

        /// <summary>Initializes a new instance of the <see cref="RabbitMqPublisherScope"/> class.</summary>
        /// <param name="scopeId">Stable scope identifier used for logging and tracing.</param>
        /// <param name="slotLease">Owning slot lease; disposed after the channel.</param>
        /// <param name="channel">Open ephemeral channel with confirms enabled.</param>
        /// <param name="confirmTimeout">Per-publish confirm wait cap.</param>
        /// <param name="metrics">Metrics sink for publish success and failure counters.</param>
        /// <param name="logger">Logger for scope publish failures.</param>
        internal RabbitMqPublisherScope(
            Guid scopeId,
            PublisherSlotLease slotLease,
            IChannel channel,
            TimeSpan confirmTimeout,
            MessageBusMetrics metrics,
            ILogger<RabbitMqPublisherScope> logger)
        {
            ScopeId = scopeId;
            _slotLease = slotLease;
            _channel = channel;
            _confirmTimeout = confirmTimeout;
            _metrics = metrics;
            _logger = logger;
        }

        /// <summary>
        /// Gets the unique identifier assigned to this publisher scope.
        /// </summary>
        public Guid ScopeId { get; }

        /// <summary>
        /// Publishes a message and waits for broker confirmation, optionally attaching a correlation id header.
        /// </summary>
        /// <param name="exchange">Target exchange name.</param>
        /// <param name="routingKey">Routing key applied to the message.</param>
        /// <param name="body">Message payload bytes.</param>
        /// <param name="correlationId">Optional correlation id stored in the message headers.</param>
        /// <param name="cancellationToken">Caller cancellation token.</param>
        /// <returns>A value task that completes when broker confirmation arrives.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the scope has been disposed.</exception>
        /// <exception cref="MessageBusPublishConfirmTimeoutException">Thrown when confirm wait exceeds timeout while caller token remains active.</exception>
        public async ValueTask PublishAsync(
            string exchange,
            string routingKey,
            ReadOnlyMemory<byte> body,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            using Activity? activity = MessageBusTelemetry.ActivitySource.StartActivity("messagebus.publish", ActivityKind.Producer);
            _ = (activity?.SetTag("messaging.system", "rabbitmq"));
            _ = (activity?.SetTag("messaging.destination.name", exchange));
            _ = (activity?.SetTag("messaging.rabbitmq.routing_key", routingKey));
            _ = (activity?.SetTag("messagebus.scope_id", ScopeId));
            if (!string.IsNullOrWhiteSpace(correlationId))
                _ = (activity?.SetTag("messagebus.correlation_id", correlationId));

            BasicProperties properties = CreatePublishProperties(correlationId);
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_confirmTimeout);
            long confirmStart = Stopwatch.GetTimestamp();
            try
            {
                await _channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: timeoutSource.Token).ConfigureAwait(false);
                _ = (activity?.SetTag("messagebus.confirm_duration_ms", Stopwatch.GetElapsedTime(confirmStart).TotalMilliseconds));
                _metrics.RecordPublish();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _metrics.RecordConfirmTimeout();
                throw new MessageBusPublishConfirmTimeoutException(
                    $"Publisher confirm was not received within {_confirmTimeout}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string failureClass = MessageBusFailureClassifier.Classify(ex);
                _metrics.RecordPublishFailure(failureClass);
                if (_logger.IsEnabled(LogLevel.Warning))
                    LogPublishFailed(ScopeId, failureClass, exchange, routingKey, correlationId);
                throw;
            }
        }

        /// <summary>
        /// Disposes the AMQP channel and releases the underlying publisher slot.
        /// </summary>
        /// <returns>A value task that completes when channel and slot resources are released.</returns>
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

        /// <summary>
        /// Creates publish properties, cloning only when correlation headers are needed.
        /// </summary>
        /// <param name="correlationId">Optional correlation identifier for the outgoing message.</param>
        /// <returns>A reusable shared instance when no correlation id is present; otherwise a scoped clone with headers.</returns>
        private static BasicProperties CreatePublishProperties(string? correlationId)
        {
            return string.IsNullOrWhiteSpace(correlationId)
                ? PublishProperties
                : new BasicProperties
                {
                    Headers = new Dictionary<string, object?>
                    {
                        [MessageBusCorrelationHeaders.CorrelationIdHeaderName] = correlationId,
                    },
                };
        }
    }
}

