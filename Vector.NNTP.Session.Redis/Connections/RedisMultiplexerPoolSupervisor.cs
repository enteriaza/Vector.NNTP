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
        private readonly RedisMultiplexerPool _pool;
        private readonly IRedisPoolHealth _health;
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
            ArgumentNullException.ThrowIfNull(pool);
            ArgumentNullException.ThrowIfNull(health);
            ArgumentNullException.ThrowIfNull(logger);
            _pool = pool;
            _health = health;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _pool.StartAsync(cancellationToken).ConfigureAwait(false);
            if (_health is RedisPoolHealth redisHealth)
            {
                redisHealth.UpdateFromPool(_pool);
            }

            LogSupervisorStarted(_logger, _pool.Snapshot.Count);
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _pool.DisposeAsync().ConfigureAwait(false);
        }
    }
}
