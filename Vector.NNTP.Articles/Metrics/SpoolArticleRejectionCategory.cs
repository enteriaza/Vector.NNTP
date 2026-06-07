// <copyright file="SpoolArticleRejectionCategory.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: operator-facing spool rejection buckets for outcome metrics and minute throughput logs.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Coarse rejection reason used by spool article outcome counters and once-per-minute throughput logs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mapped from preprocess/postprocess failure text by <see cref="SpoolArticleRejectionClassifier"/> at the same
    /// pipeline boundaries as <see cref="Logging.INntpNewsLog.LogRejected"/>. Accepted articles are recorded separately
    /// and do not use this enum.
    /// </para>
    /// <para>
    /// OpenTelemetry tag strings are defined by <see cref="SpoolArticleRejectionMetricsTags"/> and must stay aligned with
    /// minute-log field names (<c>header</c>, <c>crc</c>, <c>crosspost</c>, <c>other</c>).
    /// </para>
    /// </remarks>
    internal enum SpoolArticleRejectionCategory
    {
        /// <summary>
        /// Header syntax, semantics, Message-ID, date, forbidden-header, or path-mutation failures.
        /// </summary>
        HeaderSyntax,

        /// <summary>
        /// yEnc section CRC validation failure on the spool postprocess path.
        /// </summary>
        Crc,

        /// <summary>
        /// Newsgroups crosspost limit exceeded per <see cref="Filters.PostFilter.PostFilterStyleOptions.MaxNewsgroupCrossposts"/>.
        /// </summary>
        Crosspost,

        /// <summary>
        /// Spam policy, size limits, queue full, disk write failures, and other rejections not in the categories above.
        /// </summary>
        Other,
    }
}
