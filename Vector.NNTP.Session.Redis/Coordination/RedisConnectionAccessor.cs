// <copyright file="RedisConnectionAccessor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using StackExchange.Redis;
using Vector.NNTP.Session.Redis.Connections;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Round-robin <see cref="IRedisConnectionAccessor"/> over <see cref="RedisMultiplexerPool"/>.
    /// </summary>
    /// <remarks>
    /// Each <see cref="GetDatabase"/> call selects the next connected multiplexer in the pool snapshot.
    /// Slow coordination operations may call <see cref="SignalScaleUp"/> to request additional multiplexers.
    /// </remarks>
    /// <param name="pool">Multiplexer pool supplying live Redis connections.</param>
    public sealed class RedisConnectionAccessor(RedisMultiplexerPool pool) : IRedisConnectionAccessor
    {
        /// <summary>
        /// Multiplexer pool used for round-robin database selection and scale-up signaling.
        /// </summary>
        private readonly RedisMultiplexerPool _pool = pool ?? throw new ArgumentNullException(nameof(pool));

        /// <summary>
        /// Gets a database handle from a live pool multiplexer.
        /// </summary>
        /// <returns>Redis database API bound to the next connected multiplexer.</returns>
        /// <exception cref="Exceptions.RedisUnavailableException">Thrown when the pool snapshot has no connected multiplexers.</exception>
        public IDatabase GetDatabase()
        {
            return _pool.GetMultiplexer().GetDatabase();
        }

        /// <summary>
        /// Signals the pool background scaler to add another multiplexer after a slow coordination call.
        /// </summary>
        /// <remarks>Safe to call from hot paths; the signal is coalesced when the scaler is already busy.</remarks>
        public void SignalScaleUp()
        {
            _pool.SignalScaleUp();
        }
    }
}
