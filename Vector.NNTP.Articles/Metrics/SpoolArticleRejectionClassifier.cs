// <copyright file="SpoolArticleRejectionClassifier.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: maps spool failure reasons to outcome rejection buckets for metrics and minute logs.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Maps preprocess, postprocess, enqueue, and write failure reasons to
    /// <see cref="SpoolArticleRejectionCategory"/> buckets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from <see cref="Storage.NntpSpoolWriterPump"/> and <see cref="Storage.NntpSpoolTransitStorage"/> when
    /// recording <see cref="NntpSpoolMetrics.RecordArticleRejected"/>. Classification uses stable substring checks against
    /// operator-facing reason strings produced by <see cref="Processing.ArticleSpoolPreprocessor"/> and
    /// <see cref="Processing.ArticleSpoolPostprocessor"/>.
    /// </para>
    /// <para><b>Thread safety:</b> Static and stateless; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal static class SpoolArticleRejectionClassifier
    {
        /// <summary>
        /// Exact postprocess failure reason emitted when yEnc CRC validation fails.
        /// </summary>
        internal const string YEncCrcFailureReason = "yEnc section CRC validation failed.";

        /// <summary>
        /// Classifies a preprocess failure reason.
        /// </summary>
        /// <param name="reason">Failure reason from <see cref="Processing.ArticleSpoolPreprocessResult"/>.</param>
        /// <returns>
        /// Always <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/> — preprocess validates header syntax and path
        /// mutation only.
        /// </returns>
        internal static SpoolArticleRejectionCategory ClassifyPreprocessFailure(string? reason)
        {
            _ = reason;
            return SpoolArticleRejectionCategory.HeaderSyntax;
        }

        /// <summary>
        /// Classifies a postprocess failure reason.
        /// </summary>
        /// <param name="reason">Failure reason from <see cref="Processing.ArticleSpoolPostprocessResult"/>.</param>
        /// <returns>
        /// <see cref="SpoolArticleRejectionCategory.Crc"/> for yEnc CRC failures;
        /// <see cref="SpoolArticleRejectionCategory.Crosspost"/> for Newsgroups limit violations;
        /// <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/> for header, Message-ID, and date validation failures;
        /// otherwise <see cref="SpoolArticleRejectionCategory.Other"/> (spam, size, and unknown reasons).
        /// </returns>
        internal static SpoolArticleRejectionCategory ClassifyPostprocessFailure(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return SpoolArticleRejectionCategory.Other;
            }

            if (reason == YEncCrcFailureReason)
            {
                return SpoolArticleRejectionCategory.Crc;
            }

            if (IsCrosspostFailure(reason))
            {
                return SpoolArticleRejectionCategory.Crosspost;
            }

            if (IsHeaderSyntaxPostprocessFailure(reason))
            {
                return SpoolArticleRejectionCategory.HeaderSyntax;
            }

            return SpoolArticleRejectionCategory.Other;
        }

        /// <summary>
        /// Classifies an enqueue-time rejection from transit storage.
        /// </summary>
        /// <param name="reason">Operator-facing reason passed to <see cref="Logging.INntpNewsLog.LogRejected"/>.</param>
        /// <returns>
        /// Always <see cref="SpoolArticleRejectionCategory.Other"/> for max-size and queue-full rejections in v1.
        /// </returns>
        internal static SpoolArticleRejectionCategory ClassifyEnqueueFailure(string reason)
        {
            _ = reason;
            return SpoolArticleRejectionCategory.Other;
        }

        /// <summary>
        /// Classifies a spool disk write failure after postprocessing succeeded.
        /// </summary>
        /// <returns>Always <see cref="SpoolArticleRejectionCategory.Other"/>.</returns>
        internal static SpoolArticleRejectionCategory ClassifyWriteFailure()
        {
            return SpoolArticleRejectionCategory.Other;
        }

        /// <summary>
        /// Detects Newsgroups crosspost limit failures from postprocessor style rules.
        /// </summary>
        /// <param name="reason">Postprocess failure reason text.</param>
        /// <returns>
        /// <see langword="true"/> when the reason matches the
        /// <c>Newsgroups header lists … (limit …)</c> pattern from style validation.
        /// </returns>
        private static bool IsCrosspostFailure(string reason)
        {
            return reason.Contains("Newsgroups header lists", StringComparison.Ordinal) &&
                reason.Contains("(limit ", StringComparison.Ordinal);
        }

        /// <summary>
        /// Detects header, Message-ID, date, and forbidden-header failures from postprocess validation.
        /// </summary>
        /// <param name="reason">Postprocess failure reason text.</param>
        /// <returns><see langword="true"/> when the reason describes header semantics rather than spam or size policy.</returns>
        private static bool IsHeaderSyntaxPostprocessFailure(string reason)
        {
            return reason.Contains("Header", StringComparison.Ordinal) ||
                reason.Contains("Message-ID", StringComparison.Ordinal) ||
                reason.Contains("header line", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("header field", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("header continuation", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("header name", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("header value", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("header terminator", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("Forbidden header", StringComparison.Ordinal) ||
                reason.Contains("Transit command Message-ID", StringComparison.Ordinal) ||
                reason.Contains("Date", StringComparison.Ordinal);
        }
    }
}
