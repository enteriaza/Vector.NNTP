// <copyright file="SpoolThroughputFeedCounts.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: per-feed accept/reject deltas for spool throughput minute logs.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Accept and categorized reject counts for one INN feed name over a single minute sampling window.
    /// </summary>
    /// <param name="Feed">
    /// Incoming feed identifier assigned when the row is materialized. Per-feed rows use names resolved by
    /// <see cref="Logging.NntpNewsFeedResolver"/> at <see cref="NntpSpoolMetrics.RecordArticleAccepted"/> or
    /// <see cref="NntpSpoolMetrics.RecordArticleRejected"/> time (for example <c>Giganews</c>, <c>local</c>, or
    /// <c>?</c> when metadata is insufficient). The global rollup row in
    /// <see cref="SpoolThroughputMinuteSnapshot.Global"/> uses <see cref="SpoolThroughputMinuteSnapshot.GlobalFeedLabel"/>
    /// instead of a real feed name.
    /// </param>
    /// <param name="Accepted">
    /// Articles committed to spool during the window — incremented when
    /// <see cref="NntpSpoolMetrics.RecordArticleAccepted"/> runs after a successful durable write, aligned with
    /// <see cref="Logging.INntpNewsLog.LogAccepted"/>.
    /// </param>
    /// <param name="HeaderSyntax">
    /// Header syntax and semantics rejections during the window. Includes all preprocess failures (classified as
    /// <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/>) and postprocess header, Message-ID, date, and
    /// forbidden-header failures mapped by <see cref="SpoolArticleRejectionClassifier.ClassifyPostprocessFailure"/>.
    /// </param>
    /// <param name="Crc">
    /// yEnc section CRC rejections during the window (<see cref="SpoolArticleRejectionCategory.Crc"/>), typically from
    /// the exact postprocess reason <see cref="SpoolArticleRejectionClassifier.YEncCrcFailureReason"/>.
    /// </param>
    /// <param name="Crosspost">
    /// Newsgroups crosspost limit rejections during the window (<see cref="SpoolArticleRejectionCategory.Crosspost"/>),
    /// detected from postprocessor style-rule failure text.
    /// </param>
    /// <param name="Other">
    /// All other final rejections during the window (<see cref="SpoolArticleRejectionCategory.Other"/>), including spam
    /// classification, configured max article size, enqueue-time queue or size faults, spool write failures, and
    /// unrecognized postprocess reasons.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Producer:</b> Immutable delta rows are constructed by <see cref="SpoolFeedOutcomeCounters.TakeSnapshotAndReset"/>
    /// when <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> drains per-feed minute buckets. The global rollup
    /// row is synthesized by summing active per-feed rows rather than maintaining separate global counters.
    /// </para>
    /// <para>
    /// <b>Consumer:</b> Rows are carried inside <see cref="SpoolThroughputMinuteSnapshot"/> and logged by
    /// <see cref="Hosting.NntpSpoolThroughputLog.EmitSnapshot"/> to the host <see cref="ILogger"/> pipeline. Feeds with
    /// <see cref="Processed"/> equal to zero are omitted from <see cref="SpoolThroughputMinuteSnapshot.Feeds"/>; the
    /// global row is still present but may carry all-zero counts when the window had no activity.
    /// </para>
    /// <para>
    /// <b>Recording path:</b> Live counters are updated from writer pumps and transit storage through
    /// <see cref="SpoolFeedOutcomeCounters.RecordAccepted"/> and
    /// <see cref="SpoolFeedOutcomeCounters.RecordRejected"/>, using categories from
    /// <see cref="SpoolArticleRejectionClassifier"/>.
    /// </para>
    /// <para>
    /// <b>Shape:</b> <c>readonly record struct</c> with init-only positional fields plus computed
    /// <see cref="Rejected"/> and <see cref="Processed"/> properties. Values are snapshots; mutating counters after
    /// construction does not occur.
    /// </para>
    /// <para><b>Threading:</b> Immutable after construction; safe to enumerate from the minute logging hosted service.</para>
    /// </remarks>
    internal readonly record struct SpoolThroughputFeedCounts(
        string Feed,
        long Accepted,
        long HeaderSyntax,
        long Crc,
        long Crosspost,
        long Other)
    {
        /// <summary>
        /// Gets the total reject count across all rejection buckets in this row.
        /// </summary>
        /// <value>
        /// <see cref="HeaderSyntax"/> + <see cref="Crc"/> + <see cref="Crosspost"/> + <see cref="Other"/>. Matches the
        /// <c>rejected</c> argument passed to throughput log helpers.
        /// </value>
        /// <remarks>
        /// Computed on read; not stored as a separate field. Each rejection increments exactly one bucket in the live
        /// counter before snapshotting.
        /// </remarks>
        internal long Rejected => HeaderSyntax + Crc + Crosspost + Other;

        /// <summary>
        /// Gets the total processed article count (accepted plus rejected) in this row.
        /// </summary>
        /// <value>
        /// <see cref="Accepted"/> + <see cref="Rejected"/>. Matches the <c>processed</c> argument passed to throughput
        /// log helpers and gates emission when zero on the global rollup row.
        /// </value>
        /// <remarks>
        /// Used by <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> to omit idle feeds from
        /// <see cref="SpoolThroughputMinuteSnapshot.Feeds"/> and by
        /// <see cref="Hosting.NntpSpoolThroughputLog.EmitSnapshot"/> to skip logging when the global row is zero.
        /// </remarks>
        internal long Processed => Accepted + Rejected;
    }
}
