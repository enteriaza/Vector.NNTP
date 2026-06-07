// <copyright file="SpoolArticleRejectionMetricsTags.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: OpenTelemetry category tag strings for spool article rejection counters.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Maps <see cref="SpoolArticleRejectionCategory"/> values to OpenTelemetry <c>category</c> tag strings on
    /// <c>nntp.spool.article.rejected</c>.
    /// </summary>
    /// <remarks>
    /// Tag values use snake_case for Prometheus-style exporters. Minute throughput logs use the same semantics with
    /// shorter field labels (<c>header</c>, <c>crc</c>, <c>crosspost</c>, <c>other</c>).
    /// </remarks>
    internal static class SpoolArticleRejectionMetricsTags
    {
        /// <summary>
        /// OpenTelemetry tag value for <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/>.
        /// </summary>
        internal const string HeaderSyntax = "header_syntax";

        /// <summary>
        /// OpenTelemetry tag value for <see cref="SpoolArticleRejectionCategory.Crc"/>.
        /// </summary>
        internal const string Crc = "crc";

        /// <summary>
        /// OpenTelemetry tag value for <see cref="SpoolArticleRejectionCategory.Crosspost"/>.
        /// </summary>
        internal const string Crosspost = "crosspost";

        /// <summary>
        /// OpenTelemetry tag value for <see cref="SpoolArticleRejectionCategory.Other"/>.
        /// </summary>
        internal const string Other = "other";

        /// <summary>
        /// Returns the OpenTelemetry <c>category</c> tag string for a rejection bucket.
        /// </summary>
        /// <param name="category">Rejection bucket recorded by <see cref="NntpSpoolMetrics.RecordArticleRejected"/>.</param>
        /// <returns>Stable snake_case tag value for metrics exporters.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="category"/> is not a defined enum value.
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
