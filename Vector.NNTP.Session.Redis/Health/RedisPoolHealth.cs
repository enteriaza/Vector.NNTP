// <copyright file="RedisPoolHealth.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using Vector.NNTP.Session.Redis.Connections;

namespace Vector.NNTP.Session.Redis.Health
{
    /// <summary>
    /// Tracks aggregate pool health from <see cref="RedisMultiplexerPool"/> snapshots.
    /// </summary>
    public sealed class RedisPoolHealth : IRedisPoolHealth
    {
        /// <summary>
        /// Gets the current health status of the Redis pool.
        /// </summary>
        public RedisPoolHealthStatus Status { get; private set; } = RedisPoolHealthStatus.Recovering;

        /// <summary>
        /// Recomputes <see cref="Status"/> from the current pool snapshot.
        /// </summary>
        /// <param name="pool">Multiplexer pool to evaluate.</param>
        public void UpdateFromPool(RedisMultiplexerPool pool)
        {
            ArgumentNullException.ThrowIfNull(pool);
            int total = pool.Snapshot.Count;
            if (total == 0)
            {
                Status = RedisPoolHealthStatus.Unhealthy;
                return;
            }

            int connected = 0;
            for (int i = 0; i < total; i++)
            {
                PooledMultiplexer entry = pool.Snapshot[i];
                if (entry.State == PooledMultiplexerState.Connected && entry.Multiplexer.IsConnected)
                {
                    connected++;
                }
            }

            Status = connected == 0
                ? RedisPoolHealthStatus.Unhealthy
                : connected < total ? RedisPoolHealthStatus.Degraded : RedisPoolHealthStatus.Healthy;
        }
    }
}
