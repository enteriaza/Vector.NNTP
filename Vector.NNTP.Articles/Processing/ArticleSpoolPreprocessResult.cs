// <copyright file="ArticleSpoolPreprocessResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: preprocessing outcome carried from ArticleSpoolPreprocessor to spool writer pumps.

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Outcome of <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> before deep postprocessing and durable spool write.
    /// </summary>
    /// <param name="ArticleBytes">
    /// Bytes associated with the preprocessing attempt. When <paramref name="Success"/> is <see langword="true"/>, the
    /// preprocessed payload passed to <see cref="ArticleSpoolPostprocessor.PostprocessAsync"/> — either the original
    /// transit array when <see cref="Sockets.Configuration.NntpServerOptions.PathAppend"/> is unset, or a new array
    /// produced by <see cref="ArticlePathHeaderMutator.PrependPathAppend"/>. When <paramref name="Success"/> is
    /// <see langword="false"/>, always the same reference as the <c>articleBytes</c> argument supplied to
    /// <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> (for logging and rejection metrics only; must not be
    /// written to disk).
    /// </param>
    /// <param name="Success">
    /// <see langword="true"/> when header syntax validation and optional <c>Path</c> hop mutation completed without
    /// error; <see langword="false"/> when validation failed or path mutation threw. When <see langword="true"/>,
    /// <paramref name="FailureReason"/> is always <see langword="null"/>.
    /// </param>
    /// <param name="FailureReason">
    /// Operator-facing failure text when <paramref name="Success"/> is <see langword="false"/>; always
    /// <see langword="null"/> on success. Sourced from <see cref="ArticleSpoolPreprocessor"/> header validation
    /// messages or path-mutation exception text prefixed with <c>Path header mutation failed:</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Producer:</b> <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> is the sole constructor of this type.
    /// Validation and path-mutation faults are expressed through <see cref="Success"/> and
    /// <see cref="FailureReason"/> rather than thrown exceptions. Only invalid <c>messageId</c> or
    /// <see langword="null"/> <c>articleBytes</c> arguments throw before a result is produced.
    /// </para>
    /// <para><b>Typical failure reasons</b> (non-exhaustive):</para>
    /// <list type="bullet">
    /// <item><description><c>Header terminator was not found.</c></description></item>
    /// <item><description><c>Too many header fields (limit 256).</c></description></item>
    /// <item><description><c>Invalid header field at line {n}: …</c> from <c>HeaderFieldValidation</c>.</description></item>
    /// <item><description><c>Path header mutation failed: …</c> when <see cref="ArticlePathHeaderMutator"/> throws.</description></item>
    /// </list>
    /// <para>
    /// <b>Consumer:</b> <see cref="Storage.NntpSpoolWriterPump"/> inspects <see cref="Success"/> immediately after
    /// <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> completes. On failure it records
    /// <see cref="Metrics.NntpSpoolMetrics.RecordPreprocessFailure"/>, logs <see cref="FailureReason"/>, classifies the
    /// rejection via <see cref="Metrics.SpoolArticleRejectionClassifier.ClassifyPreprocessFailure"/> (always
    /// <see cref="Metrics.SpoolArticleRejectionCategory.HeaderSyntax"/>), writes a rejected news log entry, releases the
    /// HistoryDB reservation, and continues draining the queue without invoking the postprocessor or persisting payload
    /// bytes. On success it forwards <see cref="ArticleBytes"/> to
    /// <see cref="ArticleSpoolPostprocessor.PostprocessAsync"/> and only then may call
    /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/>.
    /// </para>
    /// <para>
    /// <b>Shape:</b> Positional record semantics expose <see cref="ArticleBytes"/>, <see cref="Success"/>, and
    /// <see cref="FailureReason"/> as init-only properties. The type is immutable and carries no behavior.
    /// </para>
    /// <para><b>Threading:</b> Safe to pass across writer-pump tasks; no shared mutable state.</para>
    /// </remarks>
    internal sealed record ArticleSpoolPreprocessResult(
        byte[] ArticleBytes,
        bool Success,
        string? FailureReason);
}
