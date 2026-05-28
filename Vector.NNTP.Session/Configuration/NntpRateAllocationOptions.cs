// <copyright file="NntpRateAllocationOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Configuration
{
    /// <summary>
    /// Fair-share refresh cadence and anti-thrash settings (code defaults; no required JSON keys).
    /// </summary>
    public sealed class NntpRateAllocationOptions
    {
        /// <summary>
        /// Configuration section name when bound explicitly.
        /// </summary>
        public const string SectionName = "NntpRateAllocation";

        /// <summary>
        /// Gets or sets how often per-account fair-share is recomputed (Option A — refresh cadence).
        /// </summary>
        public TimeSpan RateAllocationRefreshInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets or sets session-count cache TTL for distributed reads (minimum anti-thrash; default 100 ms).
        /// </summary>
        public TimeSpan SessionCountCacheTtl { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets or sets optional hysteresis for session-count input (0 = disabled).
        /// </summary>
        public int SessionCountHysteresis { get; set; }

        /// <summary>
        /// Gets or sets minimum relative change (0–1) before updating the shaper cap.
        /// </summary>
        public double MaterialRateChangeRatio { get; set; } = 0.05;
    }
}
