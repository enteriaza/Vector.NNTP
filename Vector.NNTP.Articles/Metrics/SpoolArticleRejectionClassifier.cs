// <copyright file="SpoolArticleRejectionClassifier.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: maps spool failure reasons to outcome rejection buckets for metrics and minute logs.

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// Maps preprocess, postprocess, enqueue, and write failure reasons to
    /// <see cref="SpoolArticleRejectionCategory"/> buckets for outcome metrics and minute throughput logs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Translates operator-facing rejection text into a coarse bucket immediately before
    /// <see cref="NntpSpoolMetrics.RecordArticleRejected"/>. The same bucket drives OpenTelemetry
    /// <c>nntp.spool.article.rejected</c> tags via <see cref="SpoolArticleRejectionMetricsTags"/> and per-feed minute
    /// counters via <see cref="SpoolFeedOutcomeCounters.RecordRejected"/>.
    /// </para>
    /// <para><b>Call sites:</b></para>
    /// <list type="bullet">
    /// <item><description><see cref="Storage.NntpSpoolWriterPump"/> — <see cref="ClassifyPreprocessFailure"/>, <see cref="ClassifyPostprocessFailure"/>, and <see cref="ClassifyWriteFailure"/>.</description></item>
    /// <item><description><see cref="Storage.NntpSpoolTransitStorage"/> — <see cref="ClassifyEnqueueFailure"/> on max-size and queue-full paths.</description></item>
    /// </list>
    /// <para>
    /// Classification is intentionally string-heuristic for postprocess failures (substring checks) so metrics stay
    /// stable without coupling to exception types. Preprocess, enqueue, and write classifiers are fixed buckets today.
    /// </para>
    /// <para><b>Threading:</b> Stateless static methods; safe for concurrent writer pumps and socket threads.</para>
    /// </remarks>
    internal static class SpoolArticleRejectionClassifier
    {
        /// <summary>
        /// Exact postprocess failure reason emitted when yEnc CRC validation fails.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Literal <c>yEnc section CRC validation failed.</c> Must remain byte-identical to the string returned by
        /// <see cref="Processing.ArticleSpoolPostprocessor"/> on yEnc CRC rejection so
        /// <see cref="ClassifyPostprocessFailure"/> can map the failure to <see cref="SpoolArticleRejectionCategory.Crc"/>
        /// via exact match before substring heuristics run.
        /// </para>
        /// </remarks>
        internal const string YEncCrcFailureReason = "yEnc section CRC validation failed.";

        /// <summary>
        /// Classifies a preprocess failure reason from the spool writer pump.
        /// </summary>
        /// <param name="reason">
        /// Failure reason from <see cref="Processing.ArticleSpoolPreprocessResult.FailureReason"/>. Content is not
        /// inspected; all preprocess faults are header/path syntax failures today.
        /// </param>
        /// <returns>
        /// Always <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/> — preprocess validates header syntax and
        /// optional <c>Path</c> hop mutation only.
        /// </returns>
        /// <remarks>
        /// Called from <see cref="Storage.NntpSpoolWriterPump"/> when
        /// <see cref="Processing.ArticleSpoolPreprocessor.PreprocessAsync"/> returns a failed result. Never throws.
        /// </remarks>
        internal static SpoolArticleRejectionCategory ClassifyPreprocessFailure(string? reason)
        {
            _ = reason;
            return SpoolArticleRejectionCategory.HeaderSyntax;
        }

        /// <summary>
        /// Classifies a postprocess failure reason from the spool writer pump.
        /// </summary>
        /// <param name="reason">
        /// Failure reason from <see cref="Processing.ArticleSpoolPostprocessResult.FailureReason"/>; may be
        /// <see langword="null"/> when the postprocessor did not supply text.
        /// </param>
        /// <returns>
        /// A coarse bucket determined by the evaluation order documented in <see cref="ClassifyPostprocessFailure"/>
        /// remarks.
        /// </returns>
        /// <remarks>
        /// <para><b>Evaluation order:</b></para>
        /// <list type="number">
        /// <item><description><see langword="null"/> or whitespace → <see cref="SpoolArticleRejectionCategory.Other"/>.</description></item>
        /// <item><description>Exact match on <see cref="YEncCrcFailureReason"/> → <see cref="SpoolArticleRejectionCategory.Crc"/>.</description></item>
        /// <item><description><see cref="IsCrosspostFailure"/> → <see cref="SpoolArticleRejectionCategory.Crosspost"/>.</description></item>
        /// <item><description><see cref="IsHeaderSyntaxPostprocessFailure"/> → <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/>.</description></item>
        /// <item><description>Otherwise → <see cref="SpoolArticleRejectionCategory.Other"/> (spam scores, configured max article size, parse edge cases, and unknown text).</description></item>
        /// </list>
        /// <para>
        /// Called from <see cref="Storage.NntpSpoolWriterPump"/> when
        /// <see cref="Processing.ArticleSpoolPostprocessor.PostprocessAsync"/> returns a failed result. Never throws.
        /// </para>
        /// </remarks>
        internal static SpoolArticleRejectionCategory ClassifyPostprocessFailure(string? reason)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? SpoolArticleRejectionCategory.Other
                : reason == YEncCrcFailureReason
                ? SpoolArticleRejectionCategory.Crc
                : IsCrosspostFailure(reason)
                ? SpoolArticleRejectionCategory.Crosspost
                : IsHeaderSyntaxPostprocessFailure(reason) ? SpoolArticleRejectionCategory.HeaderSyntax : SpoolArticleRejectionCategory.Other;
        }

        /// <summary>
        /// Classifies an enqueue-time rejection from transit storage before the writer pump dequeues the item.
        /// </summary>
        /// <param name="reason">
        /// Operator-facing reason passed to <see cref="Logging.INntpNewsLog.LogRejected"/> (for example
        /// <c>Article exceeds local limit of … bytes</c> or <c>Queue full</c>). Content is not inspected in v1.
        /// </param>
        /// <returns>
        /// Always <see cref="SpoolArticleRejectionCategory.Other"/> for max-size and queue-full rejections in the
        /// current implementation.
        /// </returns>
        /// <remarks>
        /// Called from <see cref="Storage.NntpSpoolTransitStorage"/> on
        /// <see cref="Storage.NntpSpoolTransitStorage.TakeThisAsync"/> size and enqueue failures. Never throws.
        /// </remarks>
        internal static SpoolArticleRejectionCategory ClassifyEnqueueFailure(string reason)
        {
            _ = reason;
            return SpoolArticleRejectionCategory.Other;
        }

        /// <summary>
        /// Classifies a spool disk write failure after postprocessing succeeded.
        /// </summary>
        /// <returns>
        /// Always <see cref="SpoolArticleRejectionCategory.Other"/> — I/O and directory preparation faults are not
        /// subdivided on the minute throughput path today.
        /// </returns>
        /// <remarks>
        /// Called from <see cref="Storage.NntpSpoolWriterPump"/> when
        /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/> or digest directory preparation throws after a
        /// successful postprocess result. Never throws.
        /// </remarks>
        internal static SpoolArticleRejectionCategory ClassifyWriteFailure()
        {
            return SpoolArticleRejectionCategory.Other;
        }

        /// <summary>
        /// Detects Newsgroups crosspost limit failures from postprocessor style rules.
        /// </summary>
        /// <param name="reason">Postprocess failure reason text from <see cref="Processing.ArticleSpoolPostprocessor"/>.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="reason"/> contains both
        /// <c>Newsgroups header lists</c> and <c>(limit </c> substrings, matching failures such as
        /// <c>Newsgroups header lists 12 groups (limit 8).</c>
        /// </returns>
        /// <remarks>
        /// Evaluated in <see cref="ClassifyPostprocessFailure"/> after the yEnc CRC exact match and before header-syntax
        /// heuristics. Never throws.
        /// </remarks>
        private static bool IsCrosspostFailure(string reason)
        {
            return reason.Contains("Newsgroups header lists", StringComparison.Ordinal) &&
                reason.Contains("(limit ", StringComparison.Ordinal);
        }

        /// <summary>
        /// Detects header, Message-ID, date, and forbidden-header failures from postprocess validation text.
        /// </summary>
        /// <param name="reason">Postprocess failure reason text from <see cref="Processing.ArticleSpoolPostprocessor"/>.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="reason"/> contains any of the configured semantic header markers;
        /// otherwise <see langword="false"/> so spam, size, and unknown reasons fall through to
        /// <see cref="SpoolArticleRejectionCategory.Other"/>.
        /// </returns>
        /// <remarks>
        /// <para><b>Matched substrings</b> (case sensitivity as implemented):</para>
        /// <list type="bullet">
        /// <item><description><c>Header</c> (ordinal)</description></item>
        /// <item><description><c>Message-ID</c> (ordinal)</description></item>
        /// <item><description><c>header line</c>, <c>header field</c>, <c>header continuation</c>, <c>header name</c>, <c>header value</c>, <c>header terminator</c> (ordinal ignore case)</description></item>
        /// <item><description><c>Forbidden header</c> (ordinal)</description></item>
        /// <item><description><c>Transit command Message-ID</c> (ordinal)</description></item>
        /// <item><description><c>Date</c> (ordinal) — matches date header parse failures such as <c>Date header is not parseable …</c></description></item>
        /// </list>
        /// <para>
        /// Heuristic matching can classify any reason containing <c>Header</c> or <c>Date</c> as header syntax, including
        /// some parse failures from <c>TryParseArticle</c>. Spam and size policy strings are expected to miss these
        /// markers. Never throws.
        /// </para>
        /// </remarks>
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
