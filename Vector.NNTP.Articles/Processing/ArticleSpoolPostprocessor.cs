// <copyright file="ArticleSpoolPostprocessor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: deep header validation and filter checks after preprocessing, before durable spool writes.

using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Classification;
using Vector.NNTP.Articles.Scanning;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Filters.DateParser;
using Vector.NNTP.Filters.PostFilter;
using Vector.NNTP.Filters.SpamAssassin;
using Vector.NNTP.Filters.YEnc;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Performs deep NNTP article header validation and filter-backed semantic checks on dequeued transit articles.
    /// </summary>
    /// <remarks>
    /// <para><b>Role:</b> Invoked by <see cref="Storage.NntpSpoolWriterPump"/> after
    /// <see cref="ArticleSpoolPreprocessor"/> succeeds and before
    /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/>. Failures return
    /// <see cref="ArticleSpoolPostprocessResult"/> so the writer loop can log, release HistoryDB reservations, and
    /// continue draining.</para>
    /// <para><b>Pipeline:</b></para>
    /// <list type="number">
    /// <item><description>Parse headers into <see cref="PostFilterParsedArticle"/> (ordered name/value pairs and body offset).</description></item>
    /// <item><description>Validate each header name and unfolded body with <see cref="HeaderFieldValidation"/>.</description></item>
    /// <item><description>Require a parsable <c>Message-ID</c> header that matches the transit command Message-ID.</description></item>
    /// <item><description>Require a canonical date via <see cref="ArticleDateHeaderResolver"/> and <see cref="NewsDateParser"/>.</description></item>
    /// <item><description>Apply <see cref="PostFilterStyleOptions"/> shape rules from <see cref="PostFilterOptions"/>.</description></item>
    /// <item><description>Classify content via <see cref="ArticleTypeClassifier"/>; validate yEnc CRC or run SpamAssassin on small non-yEnc articles.</description></item>
    /// </list>
    /// <para>
    /// <b>Registration:</b> Singleton registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// </para>
    /// </remarks>
    internal sealed partial class ArticleSpoolPostprocessor
    {
        /// <summary>
        /// Maximum number of distinct header fields accepted during parsing.
        /// </summary>
        private const int MaxHeaderFieldCount = 256;

        /// <summary>
        /// Maximum article size eligible for SpamAssassin <c>CHECK</c> on the transit spool path (128 KiB).
        /// </summary>
        private const int SpamCheckMaxArticleBytes = 131_072;

        /// <summary>
        /// Post-filter style options governing forbidden headers, crosspost limits, and article size caps.
        /// </summary>
        private readonly PostFilterStyleOptions _styleOptions;

        /// <summary>
        /// Server identity for programmatic spamd scan header synthesis.
        /// </summary>
        private readonly NntpServerOptions _serverOptions;

        /// <summary>
        /// Remote spamd client for non-yEnc articles under <see cref="SpamCheckMaxArticleBytes"/>.
        /// </summary>
        private readonly ISpamAssassin _spamAssassin;

        /// <summary>
        /// Builds temporary spamd scan articles without mutating spool payloads.
        /// </summary>
        private readonly SpamdScanArticleBuilder _spamdScanBuilder;

        /// <summary>
        /// Category logger for filter rejection and fail-open events.
        /// </summary>
        private readonly ILogger<ArticleSpoolPostprocessor> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArticleSpoolPostprocessor"/> class.
        /// </summary>
        /// <param name="postFilterOptions">
        /// Bound post-filter options; only <see cref="PostFilterOptions.Style"/> is consulted on the transit spool path.
        /// </param>
        /// <param name="serverOptions">Bound <c>NntpServer</c> options for spamd scan header synthesis.</param>
        /// <param name="spamAssassin">spamd client for small non-yEnc articles.</param>
        /// <param name="spamdScanBuilder">Temporary scan article builder.</param>
        /// <param name="logger">Category logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when any dependency is <see langword="null"/>.</exception>
        public ArticleSpoolPostprocessor(
            IOptions<PostFilterOptions> postFilterOptions,
            IOptions<NntpServerOptions> serverOptions,
            ISpamAssassin spamAssassin,
            SpamdScanArticleBuilder spamdScanBuilder,
            ILogger<ArticleSpoolPostprocessor> logger)
        {
            ArgumentNullException.ThrowIfNull(postFilterOptions);
            ArgumentNullException.ThrowIfNull(serverOptions);
            ArgumentNullException.ThrowIfNull(spamAssassin);
            ArgumentNullException.ThrowIfNull(spamdScanBuilder);
            ArgumentNullException.ThrowIfNull(logger);
            _styleOptions = postFilterOptions.Value.Style;
            _serverOptions = serverOptions.Value;
            _spamAssassin = spamAssassin;
            _spamdScanBuilder = spamdScanBuilder;
            _logger = logger;
        }

        /// <summary>
        /// Validates parsed header semantics and filter rules for a preprocessed transit article.
        /// </summary>
        /// <param name="item">
        /// Dequeued spool item supplying the transit command Message-ID and peer origin metadata for spamd synthesis.
        /// </param>
        /// <param name="articleBytes">
        /// Preprocessed article bytes (header-validated and optionally path-mutated). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="cancellationToken">Writer pump cancellation token.</param>
        /// <returns>
        /// <see cref="ArticleSpoolPostprocessResult.Success"/> <see langword="true"/> when all checks pass; otherwise
        /// <see langword="false"/> with a <see cref="ArticleSpoolPostprocessResult.FailureReason"/> suitable for operator logs.
        /// </returns>
        /// <remarks>
        /// Semantic validation failures do not throw. Spamd protocol and connectivity failures fail open (article accepted).
        /// Only invalid <paramref name="item"/> or <paramref name="articleBytes"/> arguments throw before work begins.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> or <paramref name="articleBytes"/> is <see langword="null"/>.</exception>
        public async ValueTask<ArticleSpoolPostprocessResult> PostprocessAsync(
            NntpSpoolWriteItem item,
            byte[] articleBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(articleBytes);

            if (!TryParseArticle(articleBytes, out ParsedTransitArticle? parsed, out string? parseFailure))
            {
                return new ArticleSpoolPostprocessResult(articleBytes, Success: false, parseFailure);
            }

            if (!TryValidateHeaderSemantics(parsed!, out string? semanticsFailure))
            {
                return new ArticleSpoolPostprocessResult(articleBytes, Success: false, semanticsFailure);
            }

            if (!TryValidateMessageIdHeader(item.MessageId, parsed!, out string? messageIdFailure))
            {
                return new ArticleSpoolPostprocessResult(articleBytes, Success: false, messageIdFailure);
            }

            if (!TryValidateArticleDate(parsed!, out string? dateFailure))
            {
                return new ArticleSpoolPostprocessResult(articleBytes, Success: false, dateFailure);
            }

            if (!TryValidateStyleRules(parsed!, articleBytes.Length, out string? styleFailure))
            {
                return new ArticleSpoolPostprocessResult(articleBytes, Success: false, styleFailure);
            }

            ArticleTypeFlags articleType = ArticleTypeClassifier.Classify(articleBytes);
            bool isYEnc = (articleType & ArticleTypeFlags.YEnc) != 0;
            if (isYEnc && !ValidateYEncBody(articleBytes, parsed!.Article.BodyStart))
            {
                return new ArticleSpoolPostprocessResult(
                    articleBytes,
                    Success: false,
                    "yEnc section CRC validation failed.");
            }

            if (!isYEnc && articleBytes.Length < SpamCheckMaxArticleBytes)
            {
                ArticleSpoolPostprocessResult? spamResult = await TrySpamCheckAsync(item, articleBytes, cancellationToken)
                    .ConfigureAwait(false);
                if (spamResult is not null)
                {
                    return spamResult;
                }
            }

            return new ArticleSpoolPostprocessResult(articleBytes, Success: true, FailureReason: null, articleType);
        }

        /// <summary>
        /// Validates yEnc CRC over the article body slice (isolated from async methods for C# span rules).
        /// </summary>
        /// <param name="articleBytes">Full article bytes.</param>
        /// <param name="bodyStart">Body offset after the header terminator.</param>
        /// <returns><see langword="true"/> when CRC validation succeeds.</returns>
        private static bool ValidateYEncBody(byte[] articleBytes, int bodyStart)
        {
            return YEncSectionCrc.Validate(articleBytes.AsSpan(bodyStart));
        }

        /// <summary>
        /// Runs SpamAssassin <c>CHECK</c> against a temporary scan copy; fails open on spamd errors.
        /// </summary>
        /// <param name="item">Queue item supplying origin metadata.</param>
        /// <param name="articleBytes">Original preprocessed article bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Failure result when classified as spam; <see langword="null"/> when accepted or fail-open.</returns>
        private async ValueTask<ArticleSpoolPostprocessResult?> TrySpamCheckAsync(
            NntpSpoolWriteItem item,
            byte[] articleBytes,
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] scanBytes = _spamdScanBuilder.BuildScanArticle(articleBytes, item.Origin, _serverOptions);
                SpamdCheckResult result = await _spamAssassin.CheckAsync(scanBytes, cancellationToken).ConfigureAwait(false);
                if (result.IsSpam)
                {
                    return new ArticleSpoolPostprocessResult(
                        articleBytes,
                        Success: false,
                        $"SpamAssassin classified article as spam (score {result.Score:F1}/{result.Threshold:F1}).");
                }
            }
            catch (SpamdProtocolException ex)
            {
                LogSpamdFailedOpen(this._logger, ex, item.MessageId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSpamdUnexpectedFailure(this._logger, ex, item.MessageId);
            }

            return null;
        }

        /// <summary>
        /// Parsed transit article retaining document-order headers for <see cref="ArticleDateHeaderResolver"/>.
        /// </summary>
        /// <param name="Article">
        /// Filter-facing parsed article view built as <see cref="PostFilterParsedArticle"/> for downstream filter types.
        /// </param>
        /// <param name="OrderedHeaders">
        /// Header fields in wire order (names preserve original casing) because the case-insensitive header map does not retain
        /// ordering or duplicate fields.
        /// </param>
        /// <remarks>
        /// Internal to <see cref="ArticleSpoolPostprocessor"/> parsing only; not exposed outside the spool writer path.
        /// </remarks>
        private sealed record ParsedTransitArticle(
            PostFilterParsedArticle Article,
            IReadOnlyList<(string Name, string Value)> OrderedHeaders);

        /// <summary>
        /// Parses header fields and body offset from raw article bytes into <see cref="ParsedTransitArticle"/>.
        /// </summary>
        /// <param name="articleBytes">Full article octets.</param>
        /// <param name="parsed">When this method returns <see langword="true"/>, the parsed article view.</param>
        /// <param name="failureReason">When this method returns <see langword="false"/>, an operator-facing reason.</param>
        /// <returns><see langword="true"/> when parsing succeeds.</returns>
        /// <remarks>
        /// Header names and unfolded values are materialized as <see cref="string"/> because
        /// <see cref="PostFilterParsedArticle"/> requires <see cref="IReadOnlyDictionary{TKey, TValue}"/> of decoded
        /// strings for filter and date-resolution stages. Newline iteration uses <see cref="ArticleByteScanSimd"/>.
        /// </remarks>
        private static bool TryParseArticle(byte[] articleBytes, out ParsedTransitArticle? parsed, out string? failureReason)
        {
            parsed = null;
            failureReason = null;

            int headerEnd = ArticleByteScanSimd.FindHeaderEnd(articleBytes);
            if (headerEnd < 0)
            {
                failureReason = "Header terminator was not found.";
                return false;
            }

            int bodyStart = ArticleByteScanSimd.FindBodyStart(articleBytes);
            if (bodyStart < 0)
            {
                bodyStart = headerEnd;
                while (bodyStart < articleBytes.Length && articleBytes[bodyStart] is (byte)'\r' or (byte)'\n')
                {
                    bodyStart++;
                }
            }

            var orderedHeaders = new List<(string Name, string Value)>(32);
            var headerMap = new Dictionary<string, string>(32, StringComparer.OrdinalIgnoreCase);
            int headerLineCount = 0;
            int headerFieldCount = 0;

            string? currentName = null;
            var currentValue = new StringBuilder();

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
                headerLineCount++;

                if (lineBytes.Length == 0)
                {
                    break;
                }

                if (lineBytes[0] is (byte)' ' or (byte)'\t')
                {
                    if (currentName is null)
                    {
                        failureReason = $"Unexpected header continuation at line {headerLineCount}.";
                        return false;
                    }

                    if (currentValue.Length > 0)
                    {
                        currentValue.Append('\n');
                    }

                    currentValue.Append(Encoding.UTF8.GetString(lineBytes));
                    index = lineEnd + 1;
                    continue;
                }

                if (currentName is not null)
                {
                    if (!TryCommitHeaderField(currentName, currentValue.ToString(), headerFieldCount, orderedHeaders, headerMap, out failureReason))
                    {
                        return false;
                    }

                    headerFieldCount++;
                    currentValue.Clear();
                }

                if (!TrySplitHeaderLine(lineBytes, headerLineCount, out currentName, out string initialValue, out failureReason))
                {
                    return false;
                }

                currentValue.Append(initialValue);
                index = lineEnd + 1;
            }

            if (currentName is not null)
            {
                if (!TryCommitHeaderField(currentName, currentValue.ToString(), headerFieldCount, orderedHeaders, headerMap, out failureReason))
                {
                    return false;
                }
            }

            parsed = new ParsedTransitArticle(
                new PostFilterParsedArticle(
                    articleBytes,
                    headerLineCount,
                    headerMap,
                    bodyStart),
                orderedHeaders);
            return true;
        }

        /// <summary>
        /// Commits a completed header field into the ordered list and case-insensitive header map.
        /// </summary>
        /// <param name="name">Header field name.</param>
        /// <param name="value">Unfolded header field value.</param>
        /// <param name="committedFieldCount">Number of fields committed before this one.</param>
        /// <param name="orderedHeaders">Ordered header list for date resolution.</param>
        /// <param name="headerMap">
        /// Header map keyed by field name with <see cref="StringComparer.OrdinalIgnoreCase"/>; keys preserve wire casing.
        /// </param>
        /// <param name="failureReason">Failure reason when this method returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when the field is accepted.</returns>
        private static bool TryCommitHeaderField(
            string name,
            string value,
            int committedFieldCount,
            List<(string Name, string Value)> orderedHeaders,
            Dictionary<string, string> headerMap,
            out string? failureReason)
        {
            failureReason = null;
            if (committedFieldCount >= MaxHeaderFieldCount)
            {
                failureReason = $"Too many header fields (limit {MaxHeaderFieldCount}).";
                return false;
            }

            orderedHeaders.Add((name, value));
            headerMap[name] = value;
            return true;
        }

        /// <summary>
        /// Splits a header line into field name and initial value at the first colon.
        /// </summary>
        /// <param name="lineBytes">Raw header line bytes without line terminator.</param>
        /// <param name="lineNumber">1-based line number for diagnostics.</param>
        /// <param name="name">Parsed header name when successful.</param>
        /// <param name="initialValue">Parsed value portion when successful.</param>
        /// <param name="failureReason">Failure reason when this method returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when the line contains a valid <c>name: value</c> split.</returns>
        private static bool TrySplitHeaderLine(
            ReadOnlySpan<byte> lineBytes,
            int lineNumber,
            out string? name,
            out string initialValue,
            out string? failureReason)
        {
            name = null;
            initialValue = string.Empty;
            failureReason = null;

            int colonIndex = lineBytes.IndexOf((byte)':');
            if (colonIndex <= 0)
            {
                failureReason = $"Header line {lineNumber} is missing a field name and colon.";
                return false;
            }

            ReadOnlySpan<byte> nameBytes = lineBytes[..colonIndex];
            if (!System.Text.Unicode.Utf8.IsValid(nameBytes))
            {
                failureReason = $"Header name at line {lineNumber} is not valid UTF-8.";
                return false;
            }

            name = Encoding.UTF8.GetString(nameBytes).Trim();
            if (name.Length == 0)
            {
                failureReason = $"Header name at line {lineNumber} is empty.";
                return false;
            }

            ReadOnlySpan<byte> valueBytes = lineBytes[(colonIndex + 1)..];
            if (valueBytes.Length > 0 && valueBytes[0] == (byte)' ')
            {
                valueBytes = valueBytes[1..];
            }

            if (!System.Text.Unicode.Utf8.IsValid(valueBytes))
            {
                failureReason = $"Header value at line {lineNumber} is not valid UTF-8.";
                return false;
            }

            initialValue = Encoding.UTF8.GetString(valueBytes);
            return true;
        }

        /// <summary>
        /// Validates unfolded header names and bodies using <see cref="HeaderFieldValidation"/>.
        /// </summary>
        /// <param name="parsed">Parsed article.</param>
        /// <param name="failureReason">Failure reason when this method returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when every header field passes semantic validation.</returns>
        private static bool TryValidateHeaderSemantics(ParsedTransitArticle parsed, out string? failureReason)
        {
            failureReason = null;
            foreach ((string name, string value) in parsed.OrderedHeaders)
            {
                if (!HeaderFieldValidation.IsValidHeaderName(name))
                {
                    failureReason = $"Invalid header field name '{name}'.";
                    return false;
                }

                if (!HeaderFieldValidation.IsValidHeaderBody(value))
                {
                    failureReason = $"Invalid header field body for '{name}'.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates that the article contains a syntactically valid <c>Message-ID</c> header matching the transit token.
        /// </summary>
        /// <param name="messageId">Message identifier from the transit command.</param>
        /// <param name="parsed">Parsed article.</param>
        /// <param name="failureReason">Failure reason when this method returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when the header is present, valid, and matches <paramref name="messageId"/>.</returns>
        private static bool TryValidateMessageIdHeader(string messageId, ParsedTransitArticle parsed, out string? failureReason)
        {
            failureReason = null;
            string headerValue = parsed.Article.GetHeader("message-id");
            if (headerValue.Length == 0)
            {
                failureReason = "Required Message-ID header is missing.";
                return false;
            }

            if (!MessageIdValidation.IsValidMessageId(headerValue, stripSpaces: true))
            {
                failureReason = "Message-ID header is not a valid NNTP Message-ID.";
                return false;
            }

            if (!MessageIdValidation.IsValidMessageId(messageId, stripSpaces: true))
            {
                failureReason = "Transit command Message-ID is not valid.";
                return false;
            }

            if (!string.Equals(
                    NormalizeMessageId(headerValue),
                    NormalizeMessageId(messageId),
                    StringComparison.Ordinal))
            {
                failureReason = "Message-ID header does not match the transit command Message-ID.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates that a canonical article date can be resolved from candidate date headers.
        /// </summary>
        /// <param name="parsed">Parsed article.</param>
        /// <param name="failureReason">Failure reason when this method returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when <see cref="ArticleDateHeaderResolver"/> succeeds.</returns>
        private static bool TryValidateArticleDate(ParsedTransitArticle parsed, out string? failureReason)
        {
            failureReason = null;
            if (!ArticleDateHeaderResolver.TryGetCanonicalArticleDate(
                    parsed.OrderedHeaders,
                    out _,
                    out DateParseFailureReason dateFailure))
            {
                failureReason = dateFailure == DateParseFailureReason.None
                    ? "Required date header is missing or empty."
                    : $"Date header is not parseable ({dateFailure}).";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applies configured <see cref="PostFilterStyleOptions"/> shape checks to the parsed article.
        /// </summary>
        /// <param name="parsed">Parsed article.</param>
        /// <param name="articleByteLength">Total article byte length including headers and body.</param>
        /// <param name="failureReason">Failure reason when this method returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when style rules pass.</returns>
        private bool TryValidateStyleRules(ParsedTransitArticle parsed, int articleByteLength, out string? failureReason)
        {
            failureReason = null;

            if (_styleOptions.MaxArticleBytes > 0 && articleByteLength > _styleOptions.MaxArticleBytes)
            {
                failureReason = $"Article exceeds configured maximum size ({_styleOptions.MaxArticleBytes} bytes).";
                return false;
            }

            foreach (string forbiddenName in _styleOptions.ForbiddenHeaderNames)
            {
                if (string.IsNullOrWhiteSpace(forbiddenName))
                {
                    continue;
                }

                if (parsed.Article.Headers.ContainsKey(forbiddenName.Trim()))
                {
                    failureReason = $"Forbidden header '{forbiddenName}' is present.";
                    return false;
                }
            }

            if (_styleOptions.MaxNewsgroupCrossposts > 0)
            {
                string newsgroups = parsed.Article.GetHeader("newsgroups");
                if (newsgroups.Length > 0)
                {
                    int groupCount = CountNewsgroups(newsgroups);
                    if (groupCount > _styleOptions.MaxNewsgroupCrossposts)
                    {
                        failureReason =
                            $"Newsgroups header lists {groupCount} groups (limit {_styleOptions.MaxNewsgroupCrossposts}).";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Counts distinct non-empty newsgroup tokens in a <c>Newsgroups</c> header value.
        /// </summary>
        /// <param name="newsgroups">Raw <c>Newsgroups</c> header value.</param>
        /// <returns>Number of comma-separated groups with non-whitespace content.</returns>
        private static int CountNewsgroups(string newsgroups)
        {
            int count = 0;
            int index = 0;
            while (index <= newsgroups.Length)
            {
                int comma = newsgroups.IndexOf(',', index);
                if (comma < 0)
                {
                    comma = newsgroups.Length;
                }

                ReadOnlySpan<char> token = newsgroups.AsSpan(index, comma - index).Trim();
                if (!token.IsEmpty)
                {
                    count++;
                }

                index = comma + 1;
            }

            return count;
        }

        /// <summary>
        /// Normalizes a Message-ID token by trimming surrounding whitespace.
        /// </summary>
        /// <param name="messageId">Candidate Message-ID.</param>
        /// <returns>Trimmed token.</returns>
        private static string NormalizeMessageId(string messageId)
        {
            return messageId.Trim();
        }

    }
}
