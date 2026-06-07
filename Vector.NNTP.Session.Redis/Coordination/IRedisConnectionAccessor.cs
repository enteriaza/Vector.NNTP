// <copyright file="IRedisConnectionAccessor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Provides round-robin access to a live Redis <see cref="IDatabase"/> from the multiplexer pool.
    /// </summary>
    public interface IRedisConnectionAccessor
    {
        /// <summary>
        /// Returns a database handle from a live pool multiplexer.
        /// </summary>
        /// <returns>Redis database API.</returns>
        /// <exception cref="Exceptions.RedisUnavailableException">Thrown when the pool snapshot is empty.</exception>
        public IDatabase GetDatabase();

        /// <summary>
        /// Signals the pool background scaler to add another multiplexer after a slow coordination call.
        /// </summary>
        /// <remarks>Safe to call from hot paths; the signal is coalesced when the scaler is already busy.</remarks>
        public void SignalScaleUp();
    }
}
