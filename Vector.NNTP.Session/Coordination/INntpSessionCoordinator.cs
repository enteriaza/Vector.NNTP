// <copyright file="INntpSessionCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Distributed admission for authenticated sessions (session count and distinct source IP limits).
    /// </summary>
    /// <remarks>
    /// Rate allocation and distributed session counts are eventually consistent, not strongly synchronized in real time.
    /// Short-lived discrepancies during cluster churn are expected; sustained policy violation is not.
    /// </remarks>
    public interface INntpSessionCoordinator
    {
        /// <summary>
        /// Attempts to acquire a distributed admission slot after credential proof.
        /// </summary>
        /// <param name="policy">Granted session policy.</param>
        /// <param name="sessionId">Connection session identifier.</param>
        /// <param name="clientIpText">Client IP text for distinct-IP tracking.</param>
        /// <param name="ttlSeconds">Redis lease TTL (safety backstop).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Admission outcome.</returns>
        public ValueTask<NntpSessionAdmissionResult> TryAdmitAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            int ttlSeconds,
            CancellationToken cancellationToken);

        /// <summary>
        /// Releases a previously acquired admission slot (idempotent-safe).
        /// </summary>
        /// <param name="policy">Session policy used at admit time.</param>
        /// <param name="sessionId">Connection session identifier.</param>
        /// <param name="clientIpText">Client IP text used at admit time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when release is attempted.</returns>
        public ValueTask ReleaseAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            CancellationToken cancellationToken);
    }
}
