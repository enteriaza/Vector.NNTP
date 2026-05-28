// <copyright file="INntpRateAllocationCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Computes shared account fair-share send rates for rate-limited sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counts and caps are <b>eventually consistent</b> across the cluster during churn, Redis lag, or refresh cadence.
    /// Correctness targets bounded drift and no sustained aggregate amplification — not millisecond-global equality.
    /// </para>
    /// </remarks>
    public interface INntpRateAllocationCoordinator
    {
        /// <summary>
        /// Returns the per-session send cap for an account, respecting internal refresh cadence.
        /// </summary>
        /// <param name="policy">Authenticated session policy.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Bytes per second for this TCP session's outbound shaper; 0 disables shaping.</returns>
        public Task<long> GetPerSessionSendRateBytesPerSecondAsync(
            NntpSessionPolicy policy,
            CancellationToken cancellationToken);
    }
}
