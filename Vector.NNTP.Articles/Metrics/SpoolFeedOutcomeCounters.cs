// <copyright file="SpoolFeedOutcomeCounters.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: lock-free per-feed accept/reject buckets for minute throughput snapshots.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Lock-free counters for one feed's accept and reject buckets used by <see cref="NntpSpoolMetrics"/>.
    /// </summary>
    /// <remarks>
    /// Updated concurrently from multiple writer pumps via <see cref="Interlocked"/>. Snapshots read and reset fields
    /// atomically for once-per-minute logging.
    /// </remarks>
    internal sealed class SpoolFeedOutcomeCounters
    {
        /// <summary>
        /// Accepted articles committed to spool for this feed since the last snapshot reset.
        /// </summary>
        private long _accepted;

        /// <summary>
        /// Header syntax rejections for this feed since the last snapshot reset.
        /// </summary>
        private long _headerSyntax;

        /// <summary>
        /// yEnc CRC rejections for this feed since the last snapshot reset.
        /// </summary>
        private long _crc;

        /// <summary>
        /// Crosspost limit rejections for this feed since the last snapshot reset.
        /// </summary>
        private long _crosspost;

        /// <summary>
        /// Other rejections for this feed since the last snapshot reset.
        /// </summary>
        private long _other;

        /// <summary>
        /// Increments the accepted counter for this feed.
        /// </summary>
        internal void RecordAccepted()
        {
            _ = Interlocked.Increment(ref _accepted);
        }

        /// <summary>
        /// Increments the rejection bucket matching <paramref name="category"/> for this feed.
        /// </summary>
        /// <param name="category">Rejection bucket from <see cref="SpoolArticleRejectionClassifier"/>.</param>
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
        /// Reads current counter values and resets all buckets to zero.
        /// </summary>
        /// <param name="feed">Feed name assigned to the returned snapshot row.</param>
        /// <returns>Delta counts observed since the previous snapshot for <paramref name="feed"/>.</returns>
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
