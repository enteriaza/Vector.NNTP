// <copyright file="IRedisSessionReconciliationCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Bounded reconciliation of Redis session and IP counters after stale anchors or crashes.
    /// </summary>
    public interface IRedisSessionReconciliationCoordinator
    {
        /// <summary>
        /// Realigns session and IP sets for one account key.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Observed session count after reconciliation.</returns>
        public Task<long> ReconcileAsync(string accountKey, CancellationToken cancellationToken);
    }
}
