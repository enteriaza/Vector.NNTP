// <copyright file="NodeSessionPurgeResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Outcome of a node-scoped Redis lease purge.
    /// </summary>
    public readonly struct NodeSessionPurgeResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeSessionPurgeResult"/> struct.
        /// </summary>
        /// <param name="authLeasesPurged">Auth admission slots released.</param>
        /// <param name="transitLeasesPurged">Transit ZSET members released.</param>
        /// <param name="durationMs">Wall-clock duration in milliseconds.</param>
        /// <param name="hitIterationLimit">Whether purge stopped due to <see cref="INodeSessionRegistry.MaxPurgeIterations"/>.</param>
        /// <param name="remainingSessions">Sessions still indexed when iteration limit hit.</param>
        public NodeSessionPurgeResult(
            long authLeasesPurged,
            long transitLeasesPurged,
            double durationMs,
            bool hitIterationLimit,
            long remainingSessions)
        {
            AuthLeasesPurged = authLeasesPurged;
            TransitLeasesPurged = transitLeasesPurged;
            DurationMs = durationMs;
            HitIterationLimit = hitIterationLimit;
            RemainingSessions = remainingSessions;
        }

        /// <summary>Gets auth leases purged.</summary>
        public long AuthLeasesPurged { get; }

        /// <summary>Gets transit leases purged.</summary>
        public long TransitLeasesPurged { get; }

        /// <summary>Gets purge duration in milliseconds.</summary>
        public double DurationMs { get; }

        /// <summary>Gets a value indicating whether the iteration safety limit was reached.</summary>
        public bool HitIterationLimit { get; }

        /// <summary>Gets remaining indexed sessions when <see cref="HitIterationLimit"/> is true.</summary>
        public long RemainingSessions { get; }
    }
}
