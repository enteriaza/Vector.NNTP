// <copyright file="ArticleSpoolPreprocessor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: queue worker preprocessing before durable spool writes.

using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Scanning;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Validates transit article header field syntax and optionally prepends a <c>Path</c> hop token before deep
    /// postprocessing and spool disk write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Invoked by <see cref="Storage.NntpSpoolWriterPump"/> for each dequeued
    /// <see cref="Storage.NntpSpoolWriteItem"/> before <see cref="ArticleSpoolPostprocessor"/>. Failures are returned in
    /// <see cref="ArticleSpoolPreprocessResult"/> rather than thrown so the writer loop can log, release HistoryDB
    /// reservations, and continue draining without tearing down the pump.
    /// </para>
    /// <para>
    /// <b>Scope boundary:</b> This type performs shallow, allocation-conscious header <em>syntax</em> checks only (field
    /// name, colon, required space, UTF-8 body folding). Semantic validation — Message-ID format, date headers,
    /// Newsgroups policy, yEnc CRC, SpamAssassin, and filter style rules — belongs to
    /// <see cref="ArticleSpoolPostprocessor"/>.
    /// </para>
    /// <para><b>Pipeline (success path):</b></para>
    /// <list type="number">
    /// <item><description>Locate the header/body separator via <see cref="ArticleByteScanSimd.FindHeaderEnd"/>.</description></item>
    /// <item><description>Walk header lines with <see cref="ArticleByteScanSimd.IndexOfLineFeed"/>; validate each non-continuation line through <see cref="HeaderFieldValidation.TryValidateHeaderField(ReadOnlySpan{byte}, out string?)"/>.</description></item>
    /// <item><description>Reject articles exceeding <see cref="MaxHeaderFieldCount"/> distinct header fields.</description></item>
    /// <item><description>When <see cref="NntpServerOptions.PathAppend"/> is non-empty whitespace, mutate via <see cref="ArticlePathHeaderMutator.PrependPathAppend"/>; otherwise return the original byte array reference unchanged.</description></item>
    /// </list>
    /// <para>
    /// <b>Async shape:</b> <see cref="PreprocessAsync"/> performs only synchronous work today but returns a completed
    /// <see cref="ValueTask{TResult}"/> for allocation-free hot-path calls and future asynchronous validation without
    /// changing <see cref="Storage.NntpSpoolWriterPump"/> contracts.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Singleton registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// </para>
    /// <para><b>Threading:</b> Instance state is an immutable <see cref="NntpServerOptions"/> snapshot; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal sealed class ArticleSpoolPreprocessor
    {
        /// <summary>
        /// Maximum number of non-continuation header field lines validated per article.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Continuation lines (leading space or tab) do not increment this counter. The limit bounds CPU spent on
        /// pathological many-header articles that still fit within <see cref="NntpServerOptions.MaxArtSize"/>.
        /// Legitimate transit articles typically carry far fewer fields.
        /// </para>
        /// <para>
        /// When exceeded, <see cref="TryValidateHeaders"/> returns failure reason
        /// <c>Too many header fields (limit 256).</c>.
        /// </para>
        /// </remarks>
        private const int MaxHeaderFieldCount = 256;

        /// <summary>
        /// Server options supplying <see cref="NntpServerOptions.PathAppend"/> for optional <c>Path</c> hop mutation.
        /// </summary>
        /// <remarks>
        /// Captured from <see cref="IOptions{TOptions}.Value"/> at construction; <see cref="IOptionsMonitor{TOptions}"/>
        /// changes are not observed after the preprocessor is constructed.
        /// </remarks>
        private readonly NntpServerOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArticleSpoolPreprocessor"/> class.
        /// </summary>
        /// <param name="options">
        /// Bound NNTP server options wrapper. Only <see cref="NntpServerOptions.PathAppend"/> is read by this type
        /// today; other transit settings affect later pipeline stages.
        /// </param>
        /// <remarks>
        /// Snapshots <paramref name="options"/>.<see cref="IOptions{TOptions}.Value"/> into <see cref="_options"/> so
        /// preprocessing behavior remains stable for the process lifetime unless the host is restarted with new
        /// configuration.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        public ArticleSpoolPreprocessor(IOptions<NntpServerOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _options = options.Value;
        }

        /// <summary>
        /// Validates header syntax and applies optional <c>Path</c> hop mutation for spool persistence.
        /// </summary>
        /// <param name="messageId">
        /// Message identifier associated with the queued item. Validated for null or empty only; not compared against
        /// header content and not used by preprocessing logic. Semantic Message-ID checks run in
        /// <see cref="ArticleSpoolPostprocessor"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Raw article bytes from transit (headers, separator, optional body). Must not be <see langword="null"/>. Body
        /// bytes are not inspected during header validation.
        /// </param>
        /// <returns>
        /// A completed <see cref="ValueTask{TResult}"/> that is already finished when the method returns. On success,
        /// <see cref="ArticleSpoolPreprocessResult.Success"/> is <see langword="true"/> and
        /// <see cref="ArticleSpoolPreprocessResult.ArticleBytes"/> is either the original reference (no
        /// <c>PathAppend</c>) or a new array from <see cref="ArticlePathHeaderMutator.PrependPathAppend"/>. On failure,
        /// <see cref="ArticleSpoolPreprocessResult.Success"/> is <see langword="false"/>,
        /// <see cref="ArticleSpoolPreprocessResult.ArticleBytes"/> is always the original
        /// <paramref name="articleBytes"/> reference, and <see cref="ArticleSpoolPreprocessResult.FailureReason"/> carries
        /// an operator-facing message.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Header validation and path-mutation faults do not throw. Only invalid <paramref name="messageId"/> or
        /// <see langword="null"/> <paramref name="articleBytes"/> throw before work begins.
        /// </para>
        /// <para>
        /// Path mutation runs only when <see cref="NntpServerOptions.PathAppend"/> is non-empty after
        /// <see cref="string.IsNullOrWhiteSpace"/> trimming. Exceptions from
        /// <see cref="ArticlePathHeaderMutator.PrependPathAppend"/> are caught and converted into failure results with
        /// message <c>Path header mutation failed: …</c> rather than propagating to the writer pump.
        /// </para>
        /// <para>
        /// The returned <see cref="ValueTask{TResult}"/> never faults; callers should inspect
        /// <see cref="ArticleSpoolPreprocessResult.Success"/> instead of awaiting for exceptions from validation work.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="articleBytes"/> is <see langword="null"/>.</exception>
        public ValueTask<ArticleSpoolPreprocessResult> PreprocessAsync(string messageId, byte[] articleBytes)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            ArgumentNullException.ThrowIfNull(articleBytes);

            if (!TryValidateHeaders(articleBytes, out string? validationFailure))
            {
                return ValueTask.FromResult(
                    new ArticleSpoolPreprocessResult(
                        articleBytes,
                        Success: false,
                        validationFailure));
            }

            byte[] mutated;
            try
            {
                mutated = string.IsNullOrWhiteSpace(_options.PathAppend)
                    ? articleBytes
                    : ArticlePathHeaderMutator.PrependPathAppend(articleBytes, _options.PathAppend);
            }
            catch (Exception ex)
            {
                return ValueTask.FromResult(
                    new ArticleSpoolPreprocessResult(
                        articleBytes,
                        Success: false,
                        $"Path header mutation failed: {ex.Message}"));
            }

            return ValueTask.FromResult(
                new ArticleSpoolPreprocessResult(
                    mutated,
                    Success: true,
                    FailureReason: null));
        }

        /// <summary>
        /// Validates header field syntax from the start of the article through the header/body separator.
        /// </summary>
        /// <param name="articleBytes">Article bytes containing headers and optional body.</param>
        /// <param name="failureReason">
        /// When this method returns <see langword="false"/>, a short operator-facing reason; otherwise
        /// <see langword="null"/>. Values include <c>Header terminator was not found.</c>,
        /// <c>Too many header fields (limit 256).</c>, or <c>Invalid header field at line {n}: …</c> where the suffix
        /// is produced by <see cref="HeaderFieldValidation.TryValidateHeaderField(ReadOnlySpan{byte}, out string?)"/>
        /// (for example missing space after colon or invalid header name).
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a header terminator exists, the distinct header field count is within
        /// <see cref="MaxHeaderFieldCount"/>, and every non-continuation header line passes
        /// <see cref="HeaderFieldValidation.TryValidateHeaderField(ReadOnlySpan{byte}, out string?)"/>; otherwise
        /// <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Header lines are delimited by <c>\n</c> with optional preceding <c>\r</c>. Lines beginning with space or
        /// tab are treated as RFC 5322 continuations: they are skipped for field counting and are not passed to
        /// <see cref="HeaderFieldValidation.TryValidateHeaderField(ReadOnlySpan{byte}, out string?)"/>.
        /// </para>
        /// <para>
        /// Scanning stops at the first zero-length line within the header region bounded by
        /// <see cref="ArticleByteScanSimd.FindHeaderEnd"/>. Non-continuation lines are validated in place as UTF-8
        /// bytes without per-line string allocation except inside validation failure formatting. Body bytes beyond the
        /// separator are not read.
        /// </para>
        /// <para>Never throws; malformed articles are expressed through <paramref name="failureReason"/>.</para>
        /// </remarks>
        private static bool TryValidateHeaders(byte[] articleBytes, out string? failureReason)
        {
            failureReason = null;
            int headerEnd = ArticleByteScanSimd.FindHeaderEnd(articleBytes);
            if (headerEnd < 0)
            {
                failureReason = "Header terminator was not found.";
                return false;
            }

            int lineNumber = 0;
            int headerFieldCount = 0;
            int index = 0;
            while (index < headerEnd)
            {
                int lineEnd = ArticleByteScanSimd.IndexOfLineFeed(articleBytes, index, headerEnd);

                int contentEnd = lineEnd;
                if (contentEnd > index && articleBytes[contentEnd - 1] == '\r')
                {
                    contentEnd--;
                }

                ReadOnlySpan<byte> lineBytes = articleBytes.AsSpan(index, contentEnd - index);
                lineNumber++;
                if (lineBytes.Length == 0)
                {
                    break;
                }

                if (lineBytes[0] is not (byte)' ' and not (byte)'\t')
                {
                    headerFieldCount++;
                    if (headerFieldCount > MaxHeaderFieldCount)
                    {
                        failureReason = $"Too many header fields (limit {MaxHeaderFieldCount}).";
                        return false;
                    }

                    if (!HeaderFieldValidation.TryValidateHeaderField(lineBytes, out string? fieldFailure))
                    {
                        failureReason = $"Invalid header field at line {lineNumber}: {fieldFailure}";
                        return false;
                    }
                }

                index = lineEnd + 1;
            }

            return true;
        }
    }
}
