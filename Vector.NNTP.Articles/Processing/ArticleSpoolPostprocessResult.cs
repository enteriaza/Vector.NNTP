// <copyright file="ArticleSpoolPostprocessResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: postprocessing outcome carried from ArticleSpoolPostprocessor to spool writer pumps.

using Vector.NNTP.Articles.Classification;

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Outcome of <see cref="ArticleSpoolPostprocessor.PostprocessAsync"/> after deep header validation, filter checks,
    /// yEnc CRC validation, and optional SpamAssassin classification.
    /// </summary>
    /// <param name="ArticleBytes">
    /// Bytes associated with the postprocessing attempt. When <paramref name="Success"/> is <see langword="true"/>, the
    /// validated preprocessed payload written by <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/> — always the
    /// same reference as the <c>articleBytes</c> argument to <see cref="ArticleSpoolPostprocessor.PostprocessAsync"/>
    /// (the postprocessor does not mutate spool bytes today). When <paramref name="Success"/> is
    /// <see langword="false"/>, always that same preprocessed reference (for logging and rejection metrics only; must not
    /// be written to disk).
    /// </param>
    /// <param name="Success">
    /// <see langword="true"/> when header parsing, header semantics, Message-ID agreement, date resolution, configured
    /// style rules, yEnc CRC validation (when applicable), and SpamAssassin classification (when applicable) completed
    /// without rejection; <see langword="false"/> otherwise. When <see langword="true"/>,
    /// <paramref name="FailureReason"/> is always <see langword="null"/>.
    /// </param>
    /// <param name="FailureReason">
    /// Operator-facing failure text when <paramref name="Success"/> is <see langword="false"/>; always
    /// <see langword="null"/> on success. Sourced from <see cref="ArticleSpoolPostprocessor"/> validation helpers,
    /// <see cref="Metrics.SpoolArticleRejectionClassifier.YEncCrcFailureReason"/> for yEnc CRC faults, or SpamAssassin
    /// score text when classified as spam.
    /// </param>
    /// <param name="ArticleType">
    /// <see cref="ArticleTypeFlags"/> from <see cref="ArticleTypeClassifier.Classify"/> populated only on success for
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleTypes"/> and cancel logging in
    /// <see cref="Storage.NntpSpoolWriterPump"/>. On failure, always
    /// <see cref="ArticleTypeFlags.Default"/> because rejection results omit classification in the producer.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Producer:</b> <see cref="ArticleSpoolPostprocessor.PostprocessAsync"/> is the sole constructor of this type.
    /// Semantic validation, yEnc CRC, and spam rejection are expressed through <see cref="Success"/> and
    /// <see cref="FailureReason"/> rather than thrown exceptions. Spamd protocol and connectivity failures fail open
    /// (article accepted with a successful result). Only invalid <c>item</c> or <see langword="null"/>
    /// <c>articleBytes</c> arguments throw before a result is produced.
    /// </para>
    /// <para><b>Typical failure reasons</b> (non-exhaustive):</para>
    /// <list type="bullet">
    /// <item><description>Header parse failures from <c>TryParseArticle</c>.</description></item>
    /// <item><description>Per-header semantic validation messages from <c>TryValidateHeaderSemantics</c>.</description></item>
    /// <item><description>Message-ID header mismatch with the transit command Message-ID.</description></item>
    /// <item><description>Missing or unparsable <c>Date</c> / <c>Injection-Date</c> headers.</description></item>
    /// <item><description>Style rule violations (for example <see cref="Sockets.Configuration.NntpServerOptions.MaxArtSize"/> or crosspost limits).</description></item>
    /// <item><description><c>yEnc section CRC validation failed.</c></description></item>
    /// <item><description><c>SpamAssassin classified article as spam (score …/…).</c></description></item>
    /// </list>
    /// <para>
    /// <b>Consumer:</b> <see cref="Storage.NntpSpoolWriterPump"/> inspects <see cref="Success"/> after
    /// <see cref="ArticleSpoolPreprocessor"/> succeeds and postprocessing completes. On failure it records
    /// <see cref="Metrics.NntpSpoolMetrics.RecordPostprocessFailure"/>, logs <see cref="FailureReason"/>, classifies the
    /// rejection via <see cref="Metrics.SpoolArticleRejectionClassifier.ClassifyPostprocessFailure"/>, writes a rejected
    /// news log entry (using the preprocessed byte reference from the preprocessor result), releases the HistoryDB
    /// reservation, and continues without persisting payload bytes. On success it emits
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleTypes"/> with <see cref="ArticleType"/>, writes
    /// <see cref="ArticleBytes"/> via <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/>, and may emit cancel
    /// processing logs when <see cref="ArticleTypeFlags.Cancel"/> is set.
    /// </para>
    /// <para>
    /// <b>Shape:</b> Positional record semantics expose <see cref="ArticleBytes"/>, <see cref="Success"/>,
    /// <see cref="FailureReason"/>, and <see cref="ArticleType"/> as init-only properties. The type is immutable and
    /// carries no behavior.
    /// </para>
    /// <para><b>Threading:</b> Safe to pass across writer-pump tasks; no shared mutable state.</para>
    /// </remarks>
    internal sealed record ArticleSpoolPostprocessResult(
        byte[] ArticleBytes,
        bool Success,
        string? FailureReason,
        ArticleTypeFlags ArticleType = ArticleTypeFlags.Default);
}
