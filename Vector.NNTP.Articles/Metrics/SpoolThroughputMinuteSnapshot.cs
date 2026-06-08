// <copyright file="SpoolThroughputMinuteSnapshot.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: global and per-feed spool throughput deltas for minute logging.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Accept and categorized reject deltas captured over one minute for global and per-feed throughput logging.
    /// </summary>
    /// <param name="Global">
    /// Aggregated <see cref="SpoolThroughputFeedCounts"/> summed across all feeds with activity during the window. The
    /// <see cref="SpoolThroughputFeedCounts.Feed"/> field is <see cref="GlobalFeedLabel"/> rather than a resolved INN feed
    /// name. Rejection buckets are totals of per-feed rows, not a separate global counter source.
    /// </param>
    /// <param name="Feeds">
    /// Per-feed <see cref="SpoolThroughputFeedCounts"/> rows for feeds with
    /// <see cref="SpoolThroughputFeedCounts.Processed"/> greater than zero during the window, sorted alphabetically by
    /// <see cref="SpoolThroughputFeedCounts.Feed"/> using <see cref="StringComparison.Ordinal"/>. Feeds with no accepted
    /// or rejected articles in the window are omitted entirely.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Producer:</b> <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> is the sole constructor of this type.
    /// Each call atomically reads per-feed minute buckets, resets them to zero, builds the global rollup from active feed
    /// rows, and returns this snapshot. Concurrent writer pumps may still record outcomes while the snapshot is assembled;
    /// those events accrue in the next window.
    /// </para>
    /// <para>
    /// <b>Consumer:</b> <see cref="Hosting.NntpSpoolThroughputLogHostedService"/> invokes
    /// <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> once per minute and passes the result to
    /// <see cref="Hosting.NntpSpoolThroughputLog.EmitSnapshot"/>, which logs one global line plus one line per entry in
    /// <paramref name="Feeds"/> when <see cref="SpoolThroughputFeedCounts.Processed"/> on <paramref name="Global"/> is
    /// greater than zero.
    /// </para>
    /// <para>
    /// <b>Output channel:</b> Minute summaries go to the host <see cref="ILogger"/> pipeline (for example
    /// <c>NNTPD-.log</c> / console), not the dedicated INN <c>news-{date}.log</c> Serilog sink used by
    /// <see cref="Logging.INntpNewsLog"/>.
    /// </para>
    /// <para>
    /// <b>Shape:</b> <c>readonly record struct</c> with init-only positional properties <see cref="Global"/> and
    /// <see cref="Feeds"/>. The type carries no behavior beyond <see cref="GlobalFeedLabel"/>.
    /// </para>
    /// <para><b>Threading:</b> Immutable after construction; safe to pass from metrics to the hosted logging service.</para>
    /// </remarks>
    internal readonly record struct SpoolThroughputMinuteSnapshot(
        SpoolThroughputFeedCounts Global,
        IReadOnlyList<SpoolThroughputFeedCounts> Feeds)
    {
        /// <summary>
        /// Feed label used on the synthetic global rollup row inside <see cref="Global"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Value is <c>*</c>. Distinguishes the aggregated rollup from real feed names resolved by
        /// <see cref="Logging.NntpNewsFeedResolver"/> (for example <c>Giganews</c> or <c>local</c>) in
        /// <see cref="Feeds"/>.
        /// </para>
        /// <para>
        /// Emitted as the feed token on the global throughput log line from
        /// <see cref="Hosting.NntpSpoolThroughputLog.LogGlobalThroughput"/>; per-feed lines use each row's actual
        /// <see cref="SpoolThroughputFeedCounts.Feed"/> value instead.
        /// </para>
        /// </remarks>
        internal const string GlobalFeedLabel = "*";
    }
}
