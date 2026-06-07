// <copyright file="ArticleSpoolPostprocessResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: postprocessing outcome carried from ArticleSpoolPostprocessor to spool writer pumps.

using Vector.NNTP.Articles.Classification;

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Outcome of <see cref="ArticleSpoolPostprocessor.PostprocessAsync"/> after deep header validation and filter checks.
    /// </summary>
    /// <param name="ArticleBytes">
    /// Bytes associated with the postprocessing attempt. On success, the validated article bytes ready for durable spool
    /// write (unchanged from the preprocessor output today). On failure, typically the input bytes for logging only —
    /// callers must not write failed results to disk.
    /// </param>
    /// <param name="Success">
    /// <see langword="true"/> when header semantics, Message-ID, date, configured style checks, yEnc CRC validation, and
    /// SpamAssassin classification (when applicable) completed without rejection;
    /// <see langword="false"/> otherwise.
    /// </param>
    /// <param name="FailureReason">
    /// Operator-facing failure text when <paramref name="Success"/> is <see langword="false"/>; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <param name="ArticleType">
    /// <see cref="ArticleTypeFlags"/> from <see cref="ArticleTypeClassifier.Classify"/> when classification ran; otherwise
    /// <see cref="ArticleTypeFlags.Default"/>. Populated on success for metrics emission by
    /// <see cref="Storage.NntpSpoolWriterPump"/>.
    /// </param>
    /// <remarks>
    /// <para><b>Producer:</b> <see cref="ArticleSpoolPostprocessor.PostprocessAsync"/> is the sole constructor of this
    /// type. Validation failures are expressed through <paramref name="Success"/> and
    /// <paramref name="FailureReason"/> rather than thrown exceptions.</para>
    /// <para>
    /// <b>Consumer:</b> <see cref="Storage.NntpSpoolWriterPump"/> writes <paramref name="ArticleBytes"/> only when
    /// <paramref name="Success"/> is <see langword="true"/>, after <see cref="ArticleSpoolPreprocessor"/> succeeds.
    /// </para>
    /// </remarks>
    public sealed record ArticleSpoolPostprocessResult(
        byte[] ArticleBytes,
        bool Success,
        string? FailureReason,
        ArticleTypeFlags ArticleType = ArticleTypeFlags.Default);
}
