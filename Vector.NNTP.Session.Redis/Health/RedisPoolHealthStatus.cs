// <copyright file="RedisPoolHealthStatus.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Health
{
    /// <summary>
    /// Aggregate health of the Redis multiplexer pool.
    /// </summary>
    public enum RedisPoolHealthStatus
    {
        /// <summary>Pool is starting.</summary>
        Recovering = 0,

        /// <summary>All snapshot entries are connected.</summary>
        Healthy = 1,

        /// <summary>Some multiplexers are faulted or disconnected.</summary>
        Degraded = 2,

        /// <summary>No live multiplexers are available.</summary>
        Unhealthy = 3,
    }
}
