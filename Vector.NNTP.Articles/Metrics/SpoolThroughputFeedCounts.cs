// <copyright file="SpoolThroughputFeedCounts.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: per-feed accept/reject deltas for spool throughput minute logs.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Accept and categorized reject counts for one INN feed name over a minute sampling window.
    /// </summary>
    /// <param name="Feed">
    /// Incoming feed identifier from <see cref="Logging.NntpNewsFeedResolver"/> (for example <c>Giganews</c>,
    /// <c>local</c>, or <c>?</c>).
    /// </param>
    /// <param name="Accepted">Articles committed to spool during the window.</param>
    /// <param name="HeaderSyntax">Header syntax and semantics rejections.</param>
    /// <param name="Crc">yEnc CRC rejections.</param>
    /// <param name="Crosspost">Newsgroups crosspost limit rejections.</param>
    /// <param name="Other">All other rejections (spam, size, queue, write failures).</param>
    /// <remarks>
    /// <para>
    /// Produced by <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> for
    /// <see cref="Hosting.NntpSpoolThroughputLogHostedService"/>. Empty feeds (zero processed) are omitted from snapshots.
    /// </para>
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
        /// Gets the total reject count across all rejection buckets.
        /// </summary>
        internal long Rejected => HeaderSyntax + Crc + Crosspost + Other;

        /// <summary>
        /// Gets the total processed article count (accepted plus rejected).
        /// </summary>
        internal long Processed => Accepted + Rejected;
    }
}
