// <copyright file="SpoolArticleRejectionMetricsTags.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: OpenTelemetry category tag strings for spool article rejection counters.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Maps <see cref="SpoolArticleRejectionCategory"/> values to stable OpenTelemetry <c>category</c> tag strings on
    /// the <c>nntp.spool.article.rejected</c> counter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> <see cref="NntpSpoolMetrics.RecordArticleRejected"/> calls <see cref="GetTag"/> to translate the
    /// coarse rejection bucket from <see cref="SpoolArticleRejectionClassifier"/> into the <c>category</c> dimension on
    /// <c>nntp.spool.article.rejected</c>. Per-feed minute buckets use the enum directly via
    /// <see cref="SpoolFeedOutcomeCounters.RecordRejected"/>; only OpenTelemetry export passes through this type.
    /// </para>
    /// <para><b>Alignment with minute logs:</b> Tag semantics match
    /// <see cref="SpoolThroughputFeedCounts"/> rejection fields, but string values differ from throughput log labels:</para>
    /// <list type="table">
    /// <listheader><term>Category</term><description>OTel <c>category</c> tag</description><description>Minute log field</description></listheader>
    /// <item><term><see cref="SpoolArticleRejectionCategory.HeaderSyntax"/></term><description><see cref="HeaderSyntax"/></description><description><c>header</c></description></item>
    /// <item><term><see cref="SpoolArticleRejectionCategory.Crc"/></term><description><see cref="Crc"/></description><description><c>crc</c></description></item>
    /// <item><term><see cref="SpoolArticleRejectionCategory.Crosspost"/></term><description><see cref="Crosspost"/></description><description><c>crosspost</c></description></item>
    /// <item><term><see cref="SpoolArticleRejectionCategory.Other"/></term><description><see cref="Other"/></description><description><c>other</c></description></item>
    /// </list>
    /// <para>
    /// <b>Stability:</b> Tag strings use snake_case for Prometheus-style exporters. Changing a constant value is a
    /// breaking contract for dashboards, alerts, and tests such as
    /// <c>NntpSpoolOutcomeMetricsTests.RecordArticleOutcomes_IncrementsOpenTelemetryCounters</c>.
    /// </para>
    /// <para><b>Threading:</b> Stateless static helpers; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal static class SpoolArticleRejectionMetricsTags
    {
        /// <summary>
        /// OpenTelemetry <c>category</c> tag value for <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/>.
        /// </summary>
        /// <remarks>
        /// Literal <c>header_syntax</c>. Covers preprocess header/path failures and postprocess header, Message-ID,
        /// date, and forbidden-header rejections classified by <see cref="SpoolArticleRejectionClassifier"/>.
        /// </remarks>
        internal const string HeaderSyntax = "header_syntax";

        /// <summary>
        /// OpenTelemetry <c>category</c> tag value for <see cref="SpoolArticleRejectionCategory.Crc"/>.
        /// </summary>
        /// <remarks>
        /// Literal <c>crc</c>. Covers yEnc section CRC validation failures on the spool postprocess path.
        /// </remarks>
        internal const string Crc = "crc";

        /// <summary>
        /// OpenTelemetry <c>category</c> tag value for <see cref="SpoolArticleRejectionCategory.Crosspost"/>.
        /// </summary>
        /// <remarks>
        /// Literal <c>crosspost</c>. Covers Newsgroups crosspost limit violations from postprocessor style rules.
        /// </remarks>
        internal const string Crosspost = "crosspost";

        /// <summary>
        /// OpenTelemetry <c>category</c> tag value for <see cref="SpoolArticleRejectionCategory.Other"/>.
        /// </summary>
        /// <remarks>
        /// Literal <c>other</c>. Covers spam classification, configured size limits, enqueue-time queue or size faults,
        /// spool write failures, and unrecognized postprocess reasons.
        /// </remarks>
        internal const string Other = "other";

        /// <summary>
        /// Returns the OpenTelemetry <c>category</c> tag string for a rejection bucket.
        /// </summary>
        /// <param name="category">
        /// Rejection bucket recorded by <see cref="NntpSpoolMetrics.RecordArticleRejected"/> after classification by
        /// <see cref="SpoolArticleRejectionClassifier"/>.
        /// </param>
        /// <returns>
        /// Stable snake_case tag value attached as the <c>category</c> key on <c>nntp.spool.article.rejected</c>. Maps
        /// <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/> to <see cref="HeaderSyntax"/>,
        /// <see cref="SpoolArticleRejectionCategory.Crc"/> to <see cref="Crc"/>,
        /// <see cref="SpoolArticleRejectionCategory.Crosspost"/> to <see cref="Crosspost"/>, and
        /// <see cref="SpoolArticleRejectionCategory.Other"/> to <see cref="Other"/>.
        /// </returns>
        /// <remarks>
        /// Sole production call site is <see cref="NntpSpoolMetrics.RecordArticleRejected"/>. Tests may reference the
        /// public constants directly when asserting exported metric dimensions.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="category"/> is not a defined <see cref="SpoolArticleRejectionCategory"/> value
        /// handled by the <c>switch</c> expression.
        /// </exception>
        internal static string GetTag(SpoolArticleRejectionCategory category)
        {
            return category switch
            {
                SpoolArticleRejectionCategory.HeaderSyntax => HeaderSyntax,
                SpoolArticleRejectionCategory.Crc => Crc,
                SpoolArticleRejectionCategory.Crosspost => Crosspost,
                SpoolArticleRejectionCategory.Other => Other,
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown spool rejection category."),
            };
        }
    }
}
