// <copyright file="SpoolArticleRejectionCategory.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: operator-facing spool rejection buckets for outcome metrics and minute throughput logs.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Coarse rejection bucket assigned to final spool article rejections for outcome metrics and once-per-minute
    /// throughput logs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Summarizes operator-facing failure text into four stable buckets at the same pipeline boundaries as
    /// <see cref="Logging.INntpNewsLog.LogRejected"/>. <see cref="SpoolArticleRejectionClassifier"/> maps free-form
    /// reasons to these values immediately before <see cref="NntpSpoolMetrics.RecordArticleRejected"/>. Accepted
    /// articles are recorded separately and never carry a rejection category.
    /// </para>
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    /// <item><description>OpenTelemetry <c>nntp.spool.article.rejected</c> — <c>category</c> tag via <see cref="SpoolArticleRejectionMetricsTags.GetTag"/>.</description></item>
    /// <item><description>Per-feed minute buckets — <see cref="SpoolFeedOutcomeCounters.RecordRejected"/> increments the matching field on <see cref="SpoolThroughputFeedCounts"/>.</description></item>
    /// </list>
    /// <para><b>Bucket alignment:</b></para>
    /// <list type="table">
    /// <listheader><term>Enum value</term><description>OTel <c>category</c> tag</description><description><see cref="SpoolThroughputFeedCounts"/> field</description><description>Minute log label</description></listheader>
    /// <item><term><see cref="HeaderSyntax"/></term><description><see cref="SpoolArticleRejectionMetricsTags.HeaderSyntax"/></description><description><see cref="SpoolThroughputFeedCounts.HeaderSyntax"/></description><description><c>header</c></description></item>
    /// <item><term><see cref="Crc"/></term><description><see cref="SpoolArticleRejectionMetricsTags.Crc"/></description><description><see cref="SpoolThroughputFeedCounts.Crc"/></description><description><c>crc</c></description></item>
    /// <item><term><see cref="Crosspost"/></term><description><see cref="SpoolArticleRejectionMetricsTags.Crosspost"/></description><description><see cref="SpoolThroughputFeedCounts.Crosspost"/></description><description><c>crosspost</c></description></item>
    /// <item><term><see cref="Other"/></term><description><see cref="SpoolArticleRejectionMetricsTags.Other"/></description><description><see cref="SpoolThroughputFeedCounts.Other"/></description><description><c>other</c></description></item>
    /// </list>
    /// <para>
    /// <b>Extensibility:</b> New values require coordinated updates to <see cref="SpoolArticleRejectionClassifier"/>,
    /// <see cref="SpoolArticleRejectionMetricsTags"/>, <see cref="SpoolFeedOutcomeCounters.RecordRejected"/>, throughput
    /// log templates, and dashboards. The set is intentionally small for operator scanability.
    /// </para>
    /// </remarks>
    internal enum SpoolArticleRejectionCategory
    {
        /// <summary>
        /// Header syntax, semantics, Message-ID, date, forbidden-header, and path-mutation failures.
        /// </summary>
        /// <remarks>
        /// <para><b>Typical sources:</b></para>
        /// <list type="bullet">
        /// <item><description>All <see cref="Processing.ArticleSpoolPreprocessor"/> rejections via <see cref="SpoolArticleRejectionClassifier.ClassifyPreprocessFailure"/>.</description></item>
        /// <item><description>Postprocess parse, header semantic, Message-ID, date, and forbidden-header faults matched by <see cref="SpoolArticleRejectionClassifier.ClassifyPostprocessFailure"/> header-syntax heuristics.</description></item>
        /// </list>
        /// <para>
        /// Does not include yEnc CRC, crosspost limit, spam, configured max article size, enqueue, or disk write failures.
        /// </para>
        /// </remarks>
        HeaderSyntax,

        /// <summary>
        /// yEnc section CRC validation failure on the spool postprocess path.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Assigned when postprocess failure text exactly matches
        /// <see cref="SpoolArticleRejectionClassifier.YEncCrcFailureReason"/>. No preprocess or enqueue path emits this
        /// bucket today.
        /// </para>
        /// </remarks>
        Crc,

        /// <summary>
        /// Newsgroups crosspost limit exceeded per configured post-filter style rules.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Assigned when postprocess failure text matches the <c>Newsgroups header lists … (limit …)</c> pattern detected
        /// by <see cref="SpoolArticleRejectionClassifier.ClassifyPostprocessFailure"/> crosspost detection, typically from
        /// <see cref="Filters.PostFilter.PostFilterStyleOptions.MaxNewsgroupCrossposts"/> enforcement in
        /// <see cref="Processing.ArticleSpoolPostprocessor"/>.
        /// </para>
        /// </remarks>
        Crosspost,

        /// <summary>
        /// Spam policy, size limits, enqueue faults, disk write failures, and other rejections outside the buckets above.
        /// </summary>
        /// <remarks>
        /// <para><b>Typical sources:</b></para>
        /// <list type="bullet">
        /// <item><description>SpamAssassin classification rejections from <see cref="Processing.ArticleSpoolPostprocessor"/>.</description></item>
        /// <item><description>Configured <see cref="Sockets.Configuration.NntpServerOptions.MaxArtSize"/> violations on the postprocess path.</description></item>
        /// <item><description>Enqueue-time max-size and queue-full rejections from <see cref="Storage.NntpSpoolTransitStorage"/>.</description></item>
        /// <item><description>Spool disk write failures after successful postprocessing.</description></item>
        /// <item><description>Null or whitespace postprocess failure reasons and unrecognized failure text.</description></item>
        /// </list>
        /// </remarks>
        Other,
    }
}
