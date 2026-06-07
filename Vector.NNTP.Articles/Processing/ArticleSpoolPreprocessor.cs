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
    /// Validates transit article headers and optionally prepends a <c>Path</c> hop token before spool disk write.
    /// </summary>
    /// <remarks>
    /// <para><b>Role:</b> Invoked by <see cref="Storage.NntpSpoolWriterPump"/> for each dequeued
    /// <see cref="Storage.NntpSpoolWriteItem"/>. Failures are returned in <see cref="ArticleSpoolPreprocessResult"/>
    /// rather than thrown so the writer loop can log, release HistoryDB reservations, and continue draining.</para>
    /// <para>
    /// <b>Pipeline:</b> Header syntax validation runs first using allocation-free
    /// <see cref="HeaderFieldValidation.IsValidHeaderField(ReadOnlySpan{byte})"/> and a
    /// <see cref="MaxHeaderFieldCount"/> guard. When <see cref="NntpServerOptions.PathAppend"/> is non-empty whitespace,
    /// successful articles are mutated via <see cref="ArticlePathHeaderMutator.PrependPathAppend"/>; otherwise the
    /// input byte array may be returned unchanged on success.
    /// </para>
    /// <para>
    /// <b>Async shape:</b> <see cref="PreprocessAsync"/> performs only synchronous work today but returns
    /// <see cref="ValueTask{TResult}"/> for allocation-free hot-path calls and future asynchronous validation without
    /// changing <see cref="Storage.NntpSpoolWriterPump"/> contracts.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Singleton registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// </para>
    /// </remarks>
    internal sealed class ArticleSpoolPreprocessor
    {
        /// <summary>
        /// Maximum number of non-continuation header field lines validated per article.
        /// </summary>
        /// <remarks>
        /// Bounds CPU spent on pathological many-header articles that still fit within
        /// <see cref="NntpServerOptions.MaxArtSize"/>. Legitimate transit articles typically carry far fewer fields.
        /// </remarks>
        private const int MaxHeaderFieldCount = 256;

        /// <summary>
        /// Server options supplying <see cref="NntpServerOptions.PathAppend"/> and related transit host settings.
        /// </summary>
        /// <remarks>
        /// Captured from <see cref="IOptions{TOptions}.Value"/> at construction; options monitor changes are not
        /// observed.
        /// </remarks>
        private readonly NntpServerOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArticleSpoolPreprocessor"/> class.
        /// </summary>
        /// <param name="options">Bound NNTP server options.</param>
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
        /// Message identifier associated with the payload. Validated for null/empty only; syntax is not re-checked here.
        /// </param>
        /// <param name="articleBytes">
        /// Raw article bytes from transit (headers, separator, optional body). Must not be <see langword="null"/>.
        /// </param>
        /// <returns>
        /// A completed <see cref="ValueTask{TResult}"/> with <see cref="ArticleSpoolPreprocessResult.Success"/>
        /// <see langword="true"/> and mutated bytes when validation and mutation succeed; otherwise
        /// <see langword="false"/> with the original <paramref name="articleBytes"/> reference and a
        /// <see cref="ArticleSpoolPreprocessResult.FailureReason"/> suitable for operator logs.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Validation and path-mutation failures do not throw. Only invalid <paramref name="messageId"/> or
        /// <paramref name="articleBytes"/> arguments throw before work begins.
        /// </para>
        /// <para>
        /// Path mutation exceptions are caught and converted into failure results with message
        /// <c>Path header mutation failed: …</c>.
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
        /// When this method returns <see langword="false"/>, a short operator-facing reason including the header line
        /// number and, when available, the failing header name; otherwise <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a header terminator exists and every non-continuation header line passes
        /// <see cref="HeaderFieldValidation.IsValidHeaderField(ReadOnlySpan{byte})"/>; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Header lines are delimited by <c>\n</c> with optional preceding <c>\r</c>. Lines beginning with space or
        /// tab are treated as continuations and are not validated as new header fields.
        /// </para>
        /// <para>
        /// Non-continuation lines are validated in place as UTF-8 bytes without per-line string allocation. Articles
        /// with more than <see cref="MaxHeaderFieldCount"/> distinct header fields are rejected. Body bytes beyond the
        /// separator are not inspected.
        /// </para>
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
