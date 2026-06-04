// <copyright file="INntpTransitPeerCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Cluster-wide admission for trusted transit peer TCP sessions using Redis ZSET leases.
    /// </summary>
    public interface INntpTransitPeerCoordinator
    {
        /// <summary>
        /// Attempts to acquire a peer session slot in the cluster ZSET after purging stale members.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Globally unique session identifier.</param>
        /// <param name="maxConnections">Configured cap (0 = unlimited).</param>
        /// <param name="leaseSeconds">Stale score cutoff and lease extension window.</param>
        /// <param name="nodeName">Stable cluster node identity accepting the connection.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Admission outcome.</returns>
        ValueTask<NntpTransitPeerAdmissionResult> TryAcquireAsync(
            string peerId,
            string sessionId,
            int maxConnections,
            int leaseSeconds,
            string nodeName,
            CancellationToken cancellationToken);

        /// <summary>
        /// Releases a previously acquired peer session slot (idempotent).
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Session identifier used at acquire time.</param>
        /// <param name="nodeName">Stable cluster node identity that accepted the connection.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when release is attempted.</returns>
        ValueTask ReleaseAsync(string peerId, string sessionId, string nodeName, CancellationToken cancellationToken);

        /// <summary>
        /// Refreshes the ZSET score for a live transit peer session lease.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="nodeName">Stable cluster node identity that accepted the connection.</param>
        /// <param name="leaseSeconds">Lease window in seconds (used for score recency).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when refresh is attempted.</returns>
        ValueTask RefreshLeaseAsync(
            string peerId,
            string sessionId,
            string nodeName,
            int leaseSeconds,
            CancellationToken cancellationToken);

        /// <summary>
        /// Purges stale ZSET members and returns the current live session count for metrics reconciliation.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Live session count after purge.</returns>
        ValueTask<long> ReconcileCapacityAsync(string peerId, CancellationToken cancellationToken);
    }
}
