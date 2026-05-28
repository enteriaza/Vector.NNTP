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
    /// Initializes a new instance of the <see cref="RedisConnectionAccessor"/> class.
    /// </remarks>
    /// <param name="pool">Multiplexer pool.</param>
    public sealed class RedisConnectionAccessor(RedisMultiplexerPool pool) : IRedisConnectionAccessor
    {
        private readonly RedisMultiplexerPool _pool = pool ?? throw new ArgumentNullException(nameof(pool));

        /// <inheritdoc />
        public IDatabase GetDatabase()
        {
            return _pool.GetMultiplexer().GetDatabase();
        }

        /// <inheritdoc />
        public void SignalScaleUp()
        {
            _pool.SignalScaleUp();
        }
    }
}
