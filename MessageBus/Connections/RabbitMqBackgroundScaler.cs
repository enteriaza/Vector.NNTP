// <copyright file="RabbitMqBackgroundScaler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqBackgroundScaler.cs -- Hosted service adding TCP connections when publisher slots are exhausted.
//
// Consumes coalesced scale-up signals from ConnectionPool and opens additional connections up to MaxConnections when
// aggregate slot capacity is insufficient.
//
// Thread safety:
//   Single background loop; pool mutations are lock-protected.
//
// Logging: [LoggerMessage] partial methods in RabbitMqBackgroundScaler.Logging.cs.

using MessageBus.Configuration;

namespace MessageBus.Connections
{
    /// <summary>
    /// Hosted service that adds TCP connections when publisher slot demand exhausts capacity on existing connections.
    /// </summary>
    /// <remarks>
    /// <para><b>Trigger:</b> Coalesced signals on <see cref="ConnectionPool.ScaleUpReader"/> from
    /// <see cref="ConnectionPool.AcquirePublisherSlotAsync"/> when immediate slot acquisition fails.</para>
    /// <para><b>Scale-up rule:</b></para>
    /// <list type="number">
    ///   <item><description>Snapshot count is below <see cref="RabbitMQOptions.MaxConnections"/>.</description></item>
    ///   <item><description><see cref="ConnectionPool.GetUsedSlotCount"/> is not less than
    ///     <see cref="ConnectionPool.GetUsableSlotCapacity"/> (all eligible slots in use).</description></item>
    /// </list>
    /// <para><b>Failure handling:</b> Exceptions are logged; the scaler continues processing signals.</para>
    /// <para><b>Thread safety:</b> Single reader on the scale-up channel; pool mutations are synchronized internally.</para>
    /// </remarks>
    public sealed partial class RabbitMqBackgroundScaler : BackgroundService
    {
        /// <summary>Connection pool to scale.</summary>
        private readonly ConnectionPool _pool;

        /// <summary>RabbitMQ configuration snapshot.</summary>
        private readonly IOptions<RabbitMQOptions> _options;

        /// <summary>Logger for scale events.</summary>
        private readonly ILogger<RabbitMqBackgroundScaler> _logger;

        /// <summary>Initializes a new instance of the <see cref="RabbitMqBackgroundScaler"/> class.</summary>
        /// <param name="pool">Connection pool to scale.</param>
        /// <param name="options">RabbitMQ options (max connections, channel pool size).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public RabbitMqBackgroundScaler(
            ConnectionPool pool,
            IOptions<RabbitMQOptions> options,
            ILogger<RabbitMqBackgroundScaler> logger)
        {
            ArgumentNullException.ThrowIfNull(pool);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);
            _pool = pool;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// Processes scale-up signals until host shutdown.
        /// </summary>
        /// <param name="stoppingToken">Host shutdown token.</param>
        /// <returns>A task representing the background loop.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (bool scaleUpSignal in _pool.ScaleUpReader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    RabbitMQOptions options = _options.Value;
                    int connectionCount = _pool.Snapshot.Count;
                    if (connectionCount >= options.MaxConnections)
                        continue;
                    int capacity = _pool.GetUsableSlotCapacity();
                    int used = _pool.GetUsedSlotCount();
                    if (used < capacity)
                        continue;
                    await _pool.AddConnectionAsync(stoppingToken).ConfigureAwait(false);
                    LogScaledUp(connectionCount + 1);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogScaleError(ex);
                }
            }
        }
    }
}
