// <copyright file="ArticleSpoolPreprocessResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: preprocessing outcome carried from ArticleSpoolPreprocessor to spool writer pumps.

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Outcome of <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> before a durable spool payload write.
    /// </summary>
    /// <param name="ArticleBytes">
    /// Bytes associated with the preprocessing attempt. On success, preprocessed output ready for
    /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/> (header-validated and optionally path-mutated). On
    /// failure, typically the original input bytes for logging only — callers must not write failed results to disk.
    /// </param>
    /// <param name="Success">
    /// <see langword="true"/> when header validation and optional <c>PathAppend</c> mutation completed without error;
    /// <see langword="false"/> when validation failed or path mutation threw.
    /// </param>
    /// <param name="FailureReason">
    /// Operator-facing failure text when <paramref name="Success"/> is <see langword="false"/>; otherwise
    /// <see langword="null"/>. Sourced from header validation messages or path-mutation exception text.
    /// </param>
    /// <remarks>
    /// <para><b>Producer:</b> <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> is the sole constructor of this
    /// type. It never throws for validation or mutation failures; failures are expressed through
    /// <paramref name="Success"/> and <paramref name="FailureReason"/>.</para>
    /// <para>
    /// <b>Consumer:</b> <see cref="Storage.NntpSpoolWriterPump"/> writes <paramref name="ArticleBytes"/> only when
    /// <paramref name="Success"/> is <see langword="true"/>. On failure it records metrics, logs
    /// <paramref name="FailureReason"/>, releases the HistoryDB reservation, and continues draining the queue without
    /// persisting payload bytes.
    /// </para>
    /// <para>
    /// Positional record semantics expose <paramref name="ArticleBytes"/>, <paramref name="Success"/>, and
    /// <paramref name="FailureReason"/> as init-only properties. The type carries no behavior.
    /// </para>
    /// </remarks>
    public sealed record ArticleSpoolPreprocessResult(
        byte[] ArticleBytes,
        bool Success,
        string? FailureReason);
}
