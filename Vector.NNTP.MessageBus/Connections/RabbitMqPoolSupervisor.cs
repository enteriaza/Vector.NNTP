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
    /// <param name="pool">Connection pool to start and dispose.</param>
    /// <param name="health">Pool health aggregator.</param>
    /// <param name="options">RabbitMQ options (minimum connections, shutdown drain timeout).</param>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pool"/>, <paramref name="health"/>, or
    /// <paramref name="options"/> is <see langword="null"/>.</exception>
    internal sealed partial class RabbitMqPoolSupervisor(
        ConnectionPool pool,
        IRabbitMqPoolHealth health,
        IOptions<RabbitMQOptions> options,
        ILogger<RabbitMqPoolSupervisor> logger) : IHostedService
    {
        /// <summary>Managed connection pool.</summary>
        private readonly ConnectionPool _pool = pool ?? throw new ArgumentNullException(nameof(pool));

        /// <summary>Aggregate pool health surface updated at startup.</summary>
        private readonly IRabbitMqPoolHealth _health = health ?? throw new ArgumentNullException(nameof(health));

        /// <summary>RabbitMQ configuration snapshot.</summary>
        private readonly IOptions<RabbitMQOptions> _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>
        /// Starts the connection pool and records initial health.
        /// </summary>
        /// <param name="cancellationToken">Host startup cancellation token.</param>
        /// <returns>A task that completes after the pool starts and initial health is published.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled during pool startup.</exception>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _pool.StartAsync(cancellationToken).ConfigureAwait(false);
            RabbitMQOptions options = _options.Value;
            _health.UpdateFromPool(_pool, options);
            LogMessageBusInitialized(options.Hosts.Length, options.MinConnections, options.MaxConnections, options.EnableSsl, true);
            LogSupervisorStarted();
        }

        /// <summary>
        /// Disposes the pool, bounded by <see cref="RabbitMQOptions.MaximumShutdownDrainTimeout"/>.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown cancellation token.</param>
        /// <returns>A task that completes after the pool is disposed or the shutdown drain timeout elapses.</returns>
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
