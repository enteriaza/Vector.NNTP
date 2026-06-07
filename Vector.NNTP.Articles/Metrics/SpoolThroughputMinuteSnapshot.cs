// <copyright file="SpoolThroughputMinuteSnapshot.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: global and per-feed spool throughput deltas for minute logging.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Accept/reject deltas captured over one minute for global and per-feed throughput logging.
    /// </summary>
    /// <param name="Global">
    /// Aggregated counts summed across all feeds. The <see cref="SpoolThroughputFeedCounts.Feed"/> field is
    /// <see cref="GlobalFeedLabel"/>.
    /// </param>
    /// <param name="Feeds">
    /// Per-feed deltas with activity during the window, sorted alphabetically by feed name. Excludes zero-processed feeds.
    /// </param>
    /// <remarks>
    /// Returned by <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> and consumed by
    /// <see cref="Hosting.NntpSpoolThroughputLogHostedService"/>. Never written to the INN <c>news</c> log file.
    /// </remarks>
    internal readonly record struct SpoolThroughputMinuteSnapshot(
        SpoolThroughputFeedCounts Global,
        IReadOnlyList<SpoolThroughputFeedCounts> Feeds)
    {
        /// <summary>
        /// Feed label used on the synthetic global rollup row inside <see cref="Global"/>.
        /// </summary>
        internal const string GlobalFeedLabel = "*";
    }
}
