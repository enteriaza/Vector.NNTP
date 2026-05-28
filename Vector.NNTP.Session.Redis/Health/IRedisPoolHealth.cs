// <copyright file="IRedisPoolHealth.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Health
{
    /// <summary>
    /// Aggregate Redis pool health updated from hosted-service lifecycle paths.
    /// </summary>
    public interface IRedisPoolHealth
    {
        /// <summary>Gets current pool health status.</summary>
        public RedisPoolHealthStatus Status { get; }
    }
}
