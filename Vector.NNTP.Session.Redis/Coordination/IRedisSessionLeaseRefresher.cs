// <copyright file="IRedisSessionLeaseRefresher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Refreshes TTL on Redis session anchor keys for live authenticated sessions.
    /// </summary>
    public interface IRedisSessionLeaseRefresher
    {
        /// <summary>
        /// Extends lease TTL for a session anchor, its IP set, and node session registry metadata.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="ipText">Client IP text.</param>
        /// <param name="nodeName">Stable cluster node identity.</param>
        /// <param name="ttlSeconds">Lease TTL seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when refresh is attempted.</returns>
        public Task HeartbeatAsync(
            string accountKey,
            string sessionId,
            string ipText,
            string nodeName,
            int ttlSeconds,
            CancellationToken cancellationToken);
    }
}
