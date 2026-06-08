// <copyright file="SpoolFeedOutcomeCounters.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: lock-free per-feed accept/reject buckets for minute throughput snapshots.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Lock-free mutable counters for one feed's accept and categorized reject buckets used by
    /// <see cref="NntpSpoolMetrics"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lifecycle:</b> One instance exists per distinct feed name in
    /// <c>NntpSpoolMetrics</c>'s <c>ConcurrentDictionary</c>, created lazily by
    /// <c>GetOrAdd</c> when the first outcome for that feed is recorded. Instances are long-lived for the process
    /// lifetime; minute windows are bounded by <see cref="TakeSnapshotAndReset"/>, not by allocating new counter objects.
    /// </para>
    /// <para>
    /// <b>Writers:</b> Multiple concurrent <see cref="Storage.NntpSpoolWriterPump"/> workers and transit enqueue paths
    /// call <see cref="RecordAccepted"/> and <see cref="RecordRejected"/> through
    /// <see cref="NntpSpoolMetrics.RecordArticleAccepted"/> and
    /// <see cref="NntpSpoolMetrics.RecordArticleRejected"/>. Feed names are resolved by
    /// <see cref="Logging.NntpNewsFeedResolver"/> before the bucket is selected.
    /// </para>
    /// <para>
    /// <b>Reader:</b> <see cref="Hosting.NntpSpoolThroughputLogHostedService"/> triggers
    /// <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> once per minute, which calls
    /// <see cref="TakeSnapshotAndReset"/> on every known feed bucket. Outcomes recorded while a snapshot is draining
    /// accrue in the next window.
    /// </para>
    /// <para>
    /// <b>Parallel metrics:</b> OpenTelemetry counters on <see cref="NntpSpoolMetrics"/> are updated in the same
    /// record methods; this type supplies the per-feed minute-log deltas materialized as
    /// <see cref="SpoolThroughputFeedCounts"/>.
    /// </para>
    /// <para><b>Threading:</b> All increments use <see cref="Interlocked"/>; no instance-level locks.</para>
    /// </remarks>
    internal sealed class SpoolFeedOutcomeCounters
    {
        /// <summary>
        /// Accepted articles committed to spool for this feed since the last <see cref="TakeSnapshotAndReset"/>.
        /// </summary>
        /// <remarks>
        /// Maps to <see cref="SpoolThroughputFeedCounts.Accepted"/> on snapshot. Incremented by
        /// <see cref="RecordAccepted"/> only after a successful durable spool write path.
        /// </remarks>
        private long _accepted;

        /// <summary>
        /// Header syntax and semantics rejections for this feed since the last <see cref="TakeSnapshotAndReset"/>.
        /// </summary>
        /// <remarks>
        /// Maps to <see cref="SpoolThroughputFeedCounts.HeaderSyntax"/>. Receives
        /// <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/> from preprocess failures and qualifying postprocess
        /// validation faults.
        /// </remarks>
        private long _headerSyntax;

        /// <summary>
        /// yEnc CRC rejections for this feed since the last <see cref="TakeSnapshotAndReset"/>.
        /// </summary>
        /// <remarks>
        /// Maps to <see cref="SpoolThroughputFeedCounts.Crc"/>. Receives
        /// <see cref="SpoolArticleRejectionCategory.Crc"/> only.
        /// </remarks>
        private long _crc;

        /// <summary>
        /// Newsgroups crosspost limit rejections for this feed since the last <see cref="TakeSnapshotAndReset"/>.
        /// </summary>
        /// <remarks>
        /// Maps to <see cref="SpoolThroughputFeedCounts.Crosspost"/>. Receives
        /// <see cref="SpoolArticleRejectionCategory.Crosspost"/> only.
        /// </remarks>
        private long _crosspost;

        /// <summary>
        /// Other rejections for this feed since the last <see cref="TakeSnapshotAndReset"/>.
        /// </summary>
        /// <remarks>
        /// Maps to <see cref="SpoolThroughputFeedCounts.Other"/>. Receives
        /// <see cref="SpoolArticleRejectionCategory.Other"/> and is the default bucket in
        /// <see cref="RecordRejected"/> when no more specific category matches.
        /// </remarks>
        private long _other;

        /// <summary>
        /// Increments the accepted counter for this feed bucket.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called from <see cref="NntpSpoolMetrics.RecordArticleAccepted"/> after OpenTelemetry
        /// <c>nntp.spool.article.accepted</c> is updated. Never throws.
        /// </para>
        /// <para>Uses <see cref="Interlocked"/> increment on <see cref="_accepted"/>.</para>
        /// </remarks>
        internal void RecordAccepted()
        {
            _ = Interlocked.Increment(ref _accepted);
        }

        /// <summary>
        /// Increments the rejection bucket matching <paramref name="category"/> for this feed.
        /// </summary>
        /// <param name="category">
        /// Coarse bucket from <see cref="SpoolArticleRejectionClassifier"/> at the same boundary as
        /// <see cref="Logging.INntpNewsLog.LogRejected"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Called from <see cref="NntpSpoolMetrics.RecordArticleRejected"/> after OpenTelemetry
        /// <c>nntp.spool.article.rejected</c> is updated. Each final rejection increments exactly one backing field.
        /// </para>
        /// <para>Uses <see cref="Interlocked"/> increment on the selected bucket field.</para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="category"/> is not a defined <see cref="SpoolArticleRejectionCategory"/> value
        /// handled by the <c>switch</c> (defensive guard against enum extension without counter mapping).
        /// </exception>
        internal void RecordRejected(SpoolArticleRejectionCategory category)
        {
            ref long target = ref _other;
            switch (category)
            {
                case SpoolArticleRejectionCategory.HeaderSyntax:
                    target = ref _headerSyntax;
                    break;
                case SpoolArticleRejectionCategory.Crc:
                    target = ref _crc;
                    break;
                case SpoolArticleRejectionCategory.Crosspost:
                    target = ref _crosspost;
                    break;
                case SpoolArticleRejectionCategory.Other:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown spool rejection category.");
            }

            _ = Interlocked.Increment(ref target);
        }

        /// <summary>
        /// Reads current counter values for this feed and resets all buckets to zero.
        /// </summary>
        /// <param name="feed">
        /// Feed name assigned to the returned <see cref="SpoolThroughputFeedCounts.Feed"/> field — typically the
        /// <c>ConcurrentDictionary</c> key from <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/>.
        /// </param>
        /// <returns>
        /// Immutable delta row containing counts observed since the previous snapshot for <paramref name="feed"/>. Zero
        /// buckets are preserved explicitly (the row is not coalesced to omission here; the caller skips idle feeds when
        /// <see cref="SpoolThroughputFeedCounts.Processed"/> is zero).
        /// </returns>
        /// <remarks>
        /// <para>
        /// Each backing field is reset with <see cref="Interlocked.Exchange(ref long, long)"/> to <c>0</c>.
        /// Increments that arrive during the multi-field drain may land in either the closing or opening
        /// window depending on timing; the design tolerates that race for minute-granularity logging.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        internal SpoolThroughputFeedCounts TakeSnapshotAndReset(string feed)
        {
            long accepted = Interlocked.Exchange(ref _accepted, 0);
            long headerSyntax = Interlocked.Exchange(ref _headerSyntax, 0);
            long crc = Interlocked.Exchange(ref _crc, 0);
            long crosspost = Interlocked.Exchange(ref _crosspost, 0);
            long other = Interlocked.Exchange(ref _other, 0);
            return new SpoolThroughputFeedCounts(feed, accepted, headerSyntax, crc, crosspost, other);
        }
    }
}
