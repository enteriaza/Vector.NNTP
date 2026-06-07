// <copyright file="RabbitMqPublisherPool.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPublisherPool.cs -- Ephemeral publisher channel factory on pooled TCP slot permits.
//
// CreateScopeAsync waits for a ConnectionPool slot, opens one IChannel with publisher confirmations, and returns
// RabbitMqPublisherScope. Slots are released when the scope is disposed. No internal publish retry.
//
// Thread safety:
//   Safe for concurrent CreateScopeAsync; returned IPublisherScope instances are not thread-safe.
//
// Cross-platform:
//   Portable BCL + RabbitMQ.Client 7; Windows x64 and Linux x64 on .NET 8.
//
// Logging: [LoggerMessage] partial methods in RabbitMqPublisherPool.Logging.cs.

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Exceptions;
using Vector.NNTP.MessageBus.Metrics;
using RabbitMQ.Client;

namespace Vector.NNTP.MessageBus.Publishing
{
    /// <summary>
    /// Publisher pool using ephemeral <see cref="IChannel"/> instances per <see cref="IPublisherScope"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Layering:</b> <see cref="ConnectionPool"/> owns TCP and slot permits only; this type opens channels
    /// after <see cref="PublisherSlotLease"/> acquisition and wraps them in <see cref="RabbitMqPublisherScope"/>.</para>
    ///
    /// <para><b>Confirms:</b> Channels are created with <c>publisherConfirmationsEnabled</c> and
    /// <c>publisherConfirmationTrackingEnabled</c>. RabbitMQ.Client 7 completes confirms when
    /// <c>IChannel.BasicPublishAsync</c> is awaited.</para>
    ///
    /// <para><b>Failure handling:</b> Channel creation exceptions dispose the acquired lease before throwing
    /// <see cref="MessageBusConnectionFaultException"/> so slots are not leaked.</para>
    ///
    /// <para><b>Allocation:</b> One channel object per scope; slot wait may allocate a continuation when the pool is
    /// contended.</para>
    /// </remarks>
    internal sealed partial class RabbitMqPublisherPool : IRabbitMqPublisherPool
    {
        /// <summary>Source of publisher slot leases and TCP connections.</summary>
        private readonly ConnectionPool _pool;

        /// <summary>Publish confirm timeout and pool tuning options.</summary>
        private readonly IOptions<RabbitMQOptions> _options;

        /// <summary>Logger for scope creation diagnostics.</summary>
        private readonly ILogger<RabbitMqPublisherPool> _logger;

        /// <summary>Metrics sink for publish and slot counters.</summary>
        private readonly MessageBusMetrics _metrics;

        /// <summary>Logger factory used to create scope loggers without storing extra ILogger fields.</summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>Creates a publisher pool that opens confirms-enabled channels on leased connection slots.</summary>
        /// <param name="pool">Connection pool for slot acquisition.</param>
        /// <param name="options">RabbitMQ options (<see cref="RabbitMQOptions.PublishConfirmTimeout"/>).</param>
        /// <param name="logger">Logger for debug scope creation events.</param>
        /// <param name="loggerFactory">Factory used to create <see cref="RabbitMqPublisherScope"/> loggers.</param>
        /// <param name="metrics">Metrics sink for slot and publish counters.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public RabbitMqPublisherPool(
            ConnectionPool pool,
            IOptions<RabbitMQOptions> options,
            ILogger<RabbitMqPublisherPool> logger,
            ILoggerFactory loggerFactory,
            MessageBusMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(pool);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            ArgumentNullException.ThrowIfNull(metrics);
            _pool = pool;
            _options = options;
            _logger = logger;
            _loggerFactory = loggerFactory;
            _metrics = metrics;
        }

        /// <summary>
        /// Creates a publisher scope from a leased slot and a confirms-enabled channel.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for slot wait and channel creation.</param>
        /// <returns>A publisher scope bound to a single channel and slot lease.</returns>
        /// <exception cref="MessageBusUnavailableException">Thrown when the pool is not accepting new leases.</exception>
        /// <exception cref="MessageBusLeaseTimeoutException">Thrown when lease acquisition exceeds the configured timeout.</exception>
        /// <exception cref="MessageBusConnectionFaultException">Thrown when channel creation fails after lease acquisition.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled during slot wait or channel creation.</exception>
        async Task<IPublisherScope> IRabbitMqPublisherPool.CreateScopeAsync(CancellationToken cancellationToken)
        {
            PublisherSlotLease slot = await _pool.AcquirePublisherSlotAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Guid scopeId = Guid.NewGuid();
                CreateChannelOptions channelOptions = new(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true);
                IChannel channel = await slot.Connection.Connection
                    .CreateChannelAsync(channelOptions, cancellationToken)
                    .ConfigureAwait(false);
                _metrics.RecordSlotAcquired();
                if (_logger.IsEnabled(LogLevel.Debug))
                    LogScopeCreated(scopeId);
                RabbitMQOptions options = _options.Value;
                ILogger<RabbitMqPublisherScope> scopeLogger = _loggerFactory.CreateLogger<RabbitMqPublisherScope>();
                return new RabbitMqPublisherScope(scopeId, slot, channel, options.PublishConfirmTimeout, _metrics, scopeLogger);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await slot.DisposeAsync().ConfigureAwait(false);
                string failureClass = MessageBusFailureClassifier.Classify(ex);
                _metrics.RecordPublishFailure(failureClass);
                if (_logger.IsEnabled(LogLevel.Error))
                    LogChannelFault(failureClass);
                throw new MessageBusConnectionFaultException("Failed to create publisher channel.", ex);
            }
        }
    }
}

