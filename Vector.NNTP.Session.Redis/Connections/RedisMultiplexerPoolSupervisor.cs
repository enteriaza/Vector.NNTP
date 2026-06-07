// <copyright file="RedisMultiplexerPoolSupervisor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using Vector.NNTP.Session.Redis.Health;

namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Host lifecycle coordinator that starts and stops the <see cref="RedisMultiplexerPool"/>.
    /// </summary>
    public sealed partial class RedisMultiplexerPoolSupervisor : IHostedService
    {
        /// <summary>
        /// Multiplexer pool started at host boot and disposed on shutdown.
        /// </summary>
        private readonly RedisMultiplexerPool _pool;

        /// <summary>
        /// Health aggregator updated after the pool reaches minimum connections.
        /// </summary>
        private readonly IRedisPoolHealth _health;

        /// <summary>
        /// Logger for supervisor startup events.
        /// </summary>
        private readonly ILogger<RedisMultiplexerPoolSupervisor> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisMultiplexerPoolSupervisor"/> class.
        /// </summary>
        /// <param name="pool">Pool to start and dispose.</param>
        /// <param name="health">Pool health aggregator.</param>
        /// <param name="logger">Logger.</param>
        public RedisMultiplexerPoolSupervisor(
            RedisMultiplexerPool pool,
            IRedisPoolHealth health,
            ILogger<RedisMultiplexerPoolSupervisor> logger)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _health = health ?? throw new ArgumentNullException(nameof(health));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Starts the multiplexer pool and publishes the initial health snapshot.
        /// </summary>
        /// <param name="cancellationToken">Host startup cancellation token.</param>
        /// <returns>A task that completes when minimum connections are established.</returns>
        /// <exception cref="Exceptions.RedisUnavailableException">Thrown when the pool cannot reach minimum connections.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _pool.StartAsync(cancellationToken).ConfigureAwait(false);
            if (_health is RedisPoolHealth redisHealth)
            {
                redisHealth.UpdateFromPool(_pool);
            }

            LogSupervisorStarted(_logger, _pool.Snapshot.Count);
        }

        /// <summary>
        /// Closes and disposes all pooled multiplexers during host shutdown.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown cancellation token.</param>
        /// <returns>A task that completes when every pooled multiplexer is disposed.</returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _pool.DisposeAsync().ConfigureAwait(false);
        }
    }
}
