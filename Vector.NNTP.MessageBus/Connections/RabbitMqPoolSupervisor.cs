// <copyright file="RabbitMqPoolSupervisor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPoolSupervisor.cs -- Host lifecycle coordinator starting and draining the ConnectionPool.
//
// StartAsync opens MinConnections; StopAsync disposes the pool bounded by MaximumShutdownDrainTimeout. Scaler and
// flow-control monitor run as separate hosted services.  [LoggerMessage] partial methods live in
// RabbitMqPoolSupervisor.Logging.cs.
//
// Thread safety:
//   IHostedService entry points called by the generic host; pool dispose is serialized by the pool.

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Health;

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Host lifecycle coordinator that starts and stops the RabbitMQ <see cref="ConnectionPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Startup:</b> Establishes <see cref="RabbitMQOptions.MinConnections"/> TCP connections and publishes initial
    /// <see cref="IRabbitMqPoolHealth"/> status.</para>
    /// <para><b>Shutdown:</b></para>
    /// <list type="number">
    ///   <item><description>Stop accepting new publisher slots (via pool dispose).</description></item>
    ///   <item><description>Dispose all <see cref="PooledConnection"/> entries within
    ///     <see cref="RabbitMQOptions.MaximumShutdownDrainTimeout"/>.</description></item>
    /// </list>
    /// <para><b>Related services:</b> <see cref="RabbitMqBackgroundScaler"/> and
    /// <see cref="RabbitMqPoolFlowControlMonitor"/> run independently as hosted services.</para>
    /// </remarks>
    public sealed partial class RabbitMqPoolSupervisor : IHostedService
    {
        /// <summary>Managed connection pool.</summary>
        private readonly ConnectionPool _pool;

        /// <summary>Aggregate pool health surface updated at startup.</summary>
        private readonly IRabbitMqPoolHealth _health;

        /// <summary>RabbitMQ configuration snapshot.</summary>
        private readonly IOptions<RabbitMQOptions> _options;

        /// <summary>Logger for supervisor lifecycle events.</summary>
        private readonly ILogger<RabbitMqPoolSupervisor> _logger;

        /// <summary>Initializes a new instance of the <see cref="RabbitMqPoolSupervisor"/> class.</summary>
        /// <param name="pool">Connection pool to start and dispose.</param>
        /// <param name="health">Pool health aggregator.</param>
        /// <param name="options">RabbitMQ options (minimum connections, shutdown drain timeout).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public RabbitMqPoolSupervisor(
            ConnectionPool pool,
            IRabbitMqPoolHealth health,
            IOptions<RabbitMQOptions> options,
            ILogger<RabbitMqPoolSupervisor> logger)
        {
            ArgumentNullException.ThrowIfNull(pool);
            ArgumentNullException.ThrowIfNull(health);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);
            _pool = pool;
            _health = health;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// Starts the connection pool and records initial health.
        /// </summary>
        /// <param name="cancellationToken">Host startup cancellation token.</param>
        /// <returns>A task representing the asynchronous start operation.</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _pool.StartAsync(cancellationToken).ConfigureAwait(false);
            _health.UpdateFromPool(_pool, _options.Value);
            LogSupervisorStarted();
        }

        /// <summary>
        /// Disposes the pool, bounded by <see cref="RabbitMQOptions.MaximumShutdownDrainTimeout"/>.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown cancellation token.</param>
        /// <returns>A task representing the asynchronous stop operation.</returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenSource drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            drainCts.CancelAfter(_options.Value.MaximumShutdownDrainTimeout);
            try
            {
                await _pool.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (drainCts.IsCancellationRequested)
            {
                LogShutdownDrainTimeout();
            }
        }
    }
}
