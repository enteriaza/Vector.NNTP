// <copyright file="ArticleSpoolPostprocessor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: deep header validation and filter checks after preprocessing, before durable spool writes.

using System.Text;
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
    /// <para>
    /// <b>Role:</b> Invoked by <see cref="NntpSpoolWriterPump"/> after
    /// <see cref="ArticleSpoolPreprocessor"/> succeeds and before
    /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/>. Failures return
    /// <see cref="ArticleSpoolPostprocessResult"/> so the writer loop can log, release HistoryDB reservations, and
    /// continue draining without tearing down the pump.
    /// </para>
    /// <para>
    /// <b>Scope boundary:</b> Shallow header <em>syntax</em> validation runs in
    /// <see cref="ArticleSpoolPreprocessor"/>; this type performs parsing into filter-facing structures, per-field
    /// semantic validation, Message-ID agreement with the transit command, canonical date resolution, configured style
    /// rules, content classification, yEnc CRC verification, and optional SpamAssassin <c>CHECK</c>.
    /// </para>
    /// <para><b>Pipeline (in order):</b></para>
    /// <list type="number">
    /// <item><description>Parse headers into <see cref="PostFilterParsedArticle"/> plus wire-order <see cref="ParsedTransitArticle.OrderedHeaders"/>.</description></item>
    /// <item><description>Validate each unfolded header name and body with <see cref="HeaderFieldValidation"/>.</description></item>
    /// <item><description>Require a syntactically valid <c>Message-ID</c> header matching the transit command token.</description></item>
    /// <item><description>Require a canonical date via <see cref="ArticleDateHeaderResolver"/> and <see cref="NewsDateParser"/>.</description></item>
    /// <item><description>Apply <see cref="PostFilterStyleOptions"/> forbidden headers and crosspost limits plus <see cref="NntpServerOptions.MaxArtSize"/>.</description></item>
    /// <item><description>Classify via <see cref="ArticleTypeClassifier.Classify"/>; validate yEnc CRC when <see cref="ArticleTypeFlags.YEnc"/> is set.</description></item>
    /// <item><description>Run SpamAssassin on non-yEnc articles under <see cref="SpamCheckMaxArticleBytes"/> (fail-open on spamd faults).</description></item>
    /// </list>
    /// <para>
    /// <b>Mutations:</b> Does not modify spool payload bytes. SpamAssassin uses a temporary scan copy from
    /// <see cref="SpamdScanArticleBuilder"/> only.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Singleton registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// </para>
    /// <para>
    /// <b>Logging partial:</b> Spamd fail-open warnings live in <c>ArticleSpoolPostprocessor.Logging.cs</c>. Semantic
    /// rejections are returned to the pump without logging from this type.
    /// </para>
    /// <para><b>Threading:</b> Immutable dependency snapshots after construction; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal sealed partial class ArticleSpoolPostprocessor
    {
        /// <summary>
        /// Maximum number of distinct header fields accepted during postprocess parsing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Enforced in <see cref="TryCommitHeaderField"/> when committing parsed fields. Continuation lines do not
        /// increment the count. Matches the preprocessor <c>MaxHeaderFieldCount</c> guard (256) so pathological
        /// many-header articles are rejected consistently across both stages.
        /// </para>
        /// <para>
        /// When exceeded, parsing fails with <c>Too many header fields (limit 256).</c>
        /// </para>
        /// </remarks>
        private const int MaxHeaderFieldCount = 256;

        /// <summary>
        /// Maximum article size eligible for SpamAssassin <c>CHECK</c> on the transit spool path (131072 bytes / 128 KiB).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Compared against <c>articleBytes.Length</c> in <see cref="PostprocessAsync"/> after style validation.
        /// Articles at or above this size skip spamd even when not yEnc. yEnc articles never reach spamd regardless of
        /// size.
        /// </para>
        /// </remarks>
        private const int SpamCheckMaxArticleBytes = 131_072;

        /// <summary>
        /// Post-filter style options governing forbidden headers and newsgroup crosspost limits.
        /// </summary>
        /// <remarks>
        /// Snapshot of <see cref="PostFilterOptions.Style"/> captured at construction from
        /// <see cref="IOptions{TOptions}.Value"/>; option monitor changes are not observed.
        /// </remarks>
        private readonly PostFilterStyleOptions _styleOptions;

        /// <summary>
        /// Bound server options supplying <see cref="NntpServerOptions.MaxArtSize"/> and spamd scan header synthesis.
        /// </summary>
        /// <remarks>
        /// Snapshot captured at construction. <see cref="NntpServerOptions.MaxArtSize"/> is enforced in
        /// <see cref="TryValidateStyleRules"/> when greater than zero. Identity fields are passed to
        /// <see cref="SpamdScanArticleBuilder.BuildScanArticle"/> for synthetic <c>Received:</c> and <c>To:</c> headers.
        /// </remarks>
        private readonly NntpServerOptions _serverOptions;

        /// <summary>
        /// Remote spamd client for non-yEnc articles under <see cref="SpamCheckMaxArticleBytes"/>.
        /// </summary>
        /// <remarks>
        /// Invoked from <see cref="TrySpamCheckAsync"/> via <see cref="ISpamAssassin.CheckAsync"/>. Protocol and
        /// unexpected faults fail open; only a positive spam classification returns a rejection result.
        /// </remarks>
        private readonly ISpamAssassin _spamAssassin;

        /// <summary>
        /// Builds temporary spamd scan articles without mutating spool payloads.
        /// </summary>
        /// <remarks>
        /// Called from <see cref="TrySpamCheckAsync"/> before each eligible <c>CHECK</c>. Scan synthesis faults are
        /// logged and fail open (see logging partial).
        /// </remarks>
        private readonly SpamdScanArticleBuilder _spamdScanBuilder;

        /// <summary>
        /// Category logger for spamd fail-open diagnostics on the transit spool path.
        /// </summary>
        /// <remarks>
        /// Semantic validation and spam <em>classification</em> rejections are not logged here; they are returned in
        /// <see cref="ArticleSpoolPostprocessResult"/> and logged by <see cref="NntpSpoolWriterPump"/>. Only
        /// <c>TrySpamCheckAsync</c> fail-open paths emit warnings through the logging partial.
        /// </remarks>
        private readonly ILogger<ArticleSpoolPostprocessor> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArticleSpoolPostprocessor"/> class.
        /// </summary>
        /// <param name="postFilterOptions">
        /// Bound post-filter options wrapper. Only <see cref="PostFilterOptions.Style"/> is consulted on the transit
        /// spool path.
        /// </param>
        /// <param name="serverOptions">
        /// Bound NNTP server options wrapper supplying <see cref="NntpServerOptions.MaxArtSize"/> and spamd scan
        /// identity fields.
        /// </param>
        /// <param name="spamAssassin">spamd client for eligible non-yEnc articles.</param>
        /// <param name="spamdScanBuilder">Temporary scan article builder used before each spamd <c>CHECK</c>.</param>
        /// <param name="logger">Category logger for spamd fail-open events.</param>
        /// <remarks>
        /// Snapshots <paramref name="postFilterOptions"/> and <paramref name="serverOptions"/> values into readonly
        /// fields so postprocessing behavior remains stable for the process lifetime unless the host is restarted with
        /// new configuration.
        /// </remarks>
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
        /// Dequeued spool item supplying the transit command <c>Message-ID</c>, peer
        /// <see cref="NntpSpoolArticleOrigin"/> for spamd scan synthesis, and queue metadata. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Preprocessed article bytes (shallow-validated and optionally path-mutated by
        /// <see cref="ArticleSpoolPreprocessor"/>). Must not be <see langword="null"/>. Not modified by this method.
        /// </param>
        /// <param name="cancellationToken">
        /// Writer pump cancellation token forwarded to <see cref="TrySpamCheckAsync"/>. Worker shutdown during an
        /// in-flight spamd call propagates <see cref="OperationCanceledException"/> to the pump.
        /// </param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that completes with <see cref="ArticleSpoolPostprocessResult.Success"/>
        /// <see langword="true"/>, unchanged <paramref name="articleBytes"/>, and populated
        /// <see cref="ArticleSpoolPostprocessResult.ArticleType"/> when all checks pass; otherwise
        /// <see langword="false"/> with the original <paramref name="articleBytes"/> reference and an operator-facing
        /// <see cref="ArticleSpoolPostprocessResult.FailureReason"/>. Spamd faults fail open and yield success.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Validation failures do not throw. Only <see langword="null"/> <paramref name="item"/> or
        /// <paramref name="articleBytes"/> throw before work begins.
        /// </para>
        /// <para>
        /// yEnc CRC failure uses the exact reason <c>yEnc section CRC validation failed.</c> Spam classification
        /// failures include score and threshold text. All other failures originate from the private
        /// <c>Try*</c> validation helpers called in pipeline order above.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> or <paramref name="articleBytes"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is canceled during an in-flight spamd
        /// <c>CHECK</c>.
        /// </exception>
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
        /// Validates yEnc CRC over the article body slice.
        /// </summary>
        /// <param name="articleBytes">Full article bytes including headers and body.</param>
        /// <param name="bodyStart">
        /// First body octet offset from <see cref="PostFilterParsedArticle.BodyStart"/> after header parsing.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <see cref="YEncSectionCrc.Validate"/> succeeds over
        /// <c>articleBytes[bodyStart..]</c>; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Isolated in a synchronous static helper so <see cref="PostprocessAsync"/> can remain async without span
        /// restrictions across <c>await</c> boundaries.
        /// </para>
        /// <para>Never throws; CRC faults are expressed as postprocess rejection results by the caller.</para>
        /// </remarks>
        private static bool ValidateYEncBody(byte[] articleBytes, int bodyStart)
        {
            return YEncSectionCrc.Validate(articleBytes.AsSpan(bodyStart));
        }

        /// <summary>
        /// Runs SpamAssassin <c>CHECK</c> against a temporary scan copy; fails open on spamd errors.
        /// </summary>
        /// <param name="item">
        /// Queue item supplying <see cref="NntpSpoolWriteItem.Origin"/> and <see cref="NntpSpoolWriteItem.MessageId"/>
        /// for scan synthesis.
        /// </param>
        /// <param name="articleBytes">Preprocessed article bytes; returned unchanged on spam rejection.</param>
        /// <param name="cancellationToken">
        /// Writer pump cancellation token. Honored by <see cref="ISpamAssassin.CheckAsync"/>; worker cancellation
        /// rethrows without fail-open.
        /// </param>
        /// <returns>
        /// A spam rejection <see cref="ArticleSpoolPostprocessResult"/> when classified as spam; otherwise
        /// <see langword="null"/> meaning accept (including all fail-open fault paths).
        /// </returns>
        /// <remarks>
        /// <para>
        /// Builds a scan copy via <see cref="SpamdScanArticleBuilder.BuildScanArticle"/> then calls
        /// <see cref="ISpamAssassin.CheckAsync"/>. <see cref="SpamdProtocolException"/> is logged via
        /// <c>LogSpamdFailedOpen</c>; other exceptions (including scan-build faults) via
        /// <c>LogSpamdUnexpectedFailure</c>. Both paths return <see langword="null"/> so the article is accepted.
        /// </para>
        /// <para>
        /// Eligibility is enforced by the caller: non-yEnc and length strictly less than
        /// <see cref="SpamCheckMaxArticleBytes"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is canceled during the spamd call.
        /// </exception>
        private async ValueTask<ArticleSpoolPostprocessResult?> TrySpamCheckAsync(
            NntpSpoolWriteItem item,
            byte[] articleBytes,
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] scanBytes = _spamdScanBuilder.BuildScanArticle(articleBytes, item.Origin, _serverOptions, item.MessageId);
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
                LogSpamdFailedOpen(_logger, ex, item.MessageId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSpamdUnexpectedFailure(_logger, ex, item.MessageId);
            }

            return null;
        }

        /// <summary>
        /// Parsed transit article retaining document-order headers for <see cref="ArticleDateHeaderResolver"/>.
        /// </summary>
        /// <param name="Article">
        /// Filter-facing parsed article view built as <see cref="PostFilterParsedArticle"/> for style rules, header
        /// lookup, and body offset access.
        /// </param>
        /// <param name="OrderedHeaders">
        /// Header fields in wire order with original name casing preserved. Required because the case-insensitive
        /// <see cref="PostFilterParsedArticle.Headers"/> map does not retain ordering or duplicate fields.
        /// </param>
        /// <remarks>
        /// <para>
        /// Internal to <see cref="ArticleSpoolPostprocessor"/> parsing only; not exposed outside the spool writer path.
        /// </para>
        /// <para>
        /// Duplicate header names in the wire stream appear multiple times in <paramref name="OrderedHeaders"/>; the
        /// header map retains the last committed value per case-insensitive name.
        /// </para>
        /// </remarks>
        private sealed record ParsedTransitArticle(
            PostFilterParsedArticle Article,
            IReadOnlyList<(string Name, string Value)> OrderedHeaders);

        /// <summary>
        /// Parses header fields and body offset from raw article bytes into <see cref="ParsedTransitArticle"/>.
        /// </summary>
        /// <param name="articleBytes">Full article octets including headers, separator, and optional body.</param>
        /// <param name="parsed">
        /// When this method returns <see langword="true"/>, the parsed article view; otherwise <see langword="null"/>.
        /// </param>
        /// <param name="failureReason">
        /// When this method returns <see langword="false"/>, an operator-facing reason (for example missing header
        /// terminator, unexpected continuation, invalid UTF-8, or too many fields); otherwise <see langword="null"/>.
        /// </param>
        /// <returns><see langword="true"/> when parsing and field commits succeed.</returns>
        /// <remarks>
        /// <para>
        /// Header names and unfolded values are materialized as <see cref="string"/> because
        /// <see cref="PostFilterParsedArticle"/> requires a case-insensitive
        /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> of decoded strings for filter and date-resolution stages.
        /// Newline iteration uses <see cref="ArticleByteScanSimd.FindHeaderEnd"/>,
        /// <see cref="ArticleByteScanSimd.FindBodyStart"/>, and <see cref="ArticleByteScanSimd.IndexOfLineFeed"/>.
        /// </para>
        /// <para>
        /// A continuation line before any field name produces <c>Unexpected header continuation at line {n}.</c> Lines
        /// without a valid colon split fail via <see cref="TrySplitHeaderLine"/>.
        /// </para>
        /// <para>Never throws; malformed articles are expressed through <paramref name="failureReason"/>.</para>
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

            List<(string Name, string Value)> orderedHeaders = new(32);
            Dictionary<string, string> headerMap = new(32, StringComparer.OrdinalIgnoreCase);
            int headerLineCount = 0;
            int headerFieldCount = 0;

            string? currentName = null;
            StringBuilder currentValue = new();

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
                        _ = currentValue.Append('\n');
                    }

                    _ = currentValue.Append(Encoding.UTF8.GetString(lineBytes));
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
                    _ = currentValue.Clear();
                }

                if (!TrySplitHeaderLine(lineBytes, headerLineCount, out currentName, out string initialValue, out failureReason))
                {
                    return false;
                }

                _ = currentValue.Append(initialValue);
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
        /// <param name="name">Decoded header field name (wire casing preserved in the ordered list).</param>
        /// <param name="value">Unfolded header field value.</param>
        /// <param name="committedFieldCount">
        /// Number of fields already committed before this one; used to enforce <see cref="MaxHeaderFieldCount"/>.
        /// </param>
        /// <param name="orderedHeaders">Wire-order header list for date resolution and semantic validation walks.</param>
        /// <param name="headerMap">
        /// Header map keyed by field name with <see cref="StringComparer.OrdinalIgnoreCase"/>; keys preserve the casing
        /// of the most recently committed field with that name.
        /// </param>
        /// <param name="failureReason">
        /// When this method returns <see langword="false"/>, <c>Too many header fields (limit 256).</c>; otherwise
        /// <see langword="null"/>.
        /// </param>
        /// <returns><see langword="true"/> when the field is accepted.</returns>
        /// <remarks>
        /// Duplicate names overwrite the prior map entry while appending another ordered-list tuple. Never throws.
        /// </remarks>
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
        /// <param name="lineNumber">1-based physical line number for diagnostics.</param>
        /// <param name="name">Parsed trimmed header name when successful; otherwise <see langword="null"/>.</param>
        /// <param name="initialValue">
        /// Parsed value portion when successful (leading single space after colon stripped when present).
        /// </param>
        /// <param name="failureReason">
        /// When this method returns <see langword="false"/>, a line-specific parse reason; otherwise
        /// <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the line contains a non-empty UTF-8 name, colon, and valid UTF-8 value bytes.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Does not require the RFC 5322 mandatory space after colon for the value to be non-empty; an empty value
        /// after a valid <c>name: </c> split is accepted at this stage and may fail later in semantic body validation.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
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
        /// Validates unfolded header names and bodies using <see cref="HeaderFieldValidation"/> string overloads.
        /// </summary>
        /// <param name="parsed">Parsed article with wire-order headers.</param>
        /// <param name="failureReason">
        /// When this method returns <see langword="false"/>, either <c>Invalid header field name '{name}'.</c> or
        /// <c>Invalid header field body for '{name}'.</c>; otherwise <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when every entry in <see cref="ParsedTransitArticle.OrderedHeaders"/> passes
        /// <see cref="HeaderFieldValidation.IsValidHeaderName(string?)"/> and
        /// <see cref="HeaderFieldValidation.IsValidHeaderBody(string?)"/>.
        /// </returns>
        /// <remarks>
        /// Walks ordered headers so duplicate field names are each validated independently. Never throws.
        /// </remarks>
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
        /// <param name="messageId">
        /// Message identifier from the transit command (<see cref="NntpSpoolWriteItem.MessageId"/>). Validated with
        /// <see cref="MessageIdValidation.IsValidMessageId(string?, bool)"/> before comparison.
        /// </param>
        /// <param name="parsed">Parsed article supplying the <c>Message-ID</c> header via case-insensitive lookup.</param>
        /// <param name="failureReason">
        /// When this method returns <see langword="false"/>, a specific Message-ID fault message; otherwise
        /// <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the header is present, both tokens are valid NNTP Message-IDs, and trimmed
        /// values match under ordinal equality.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Header lookup uses <c>message-id</c> through <see cref="PostFilterParsedArticle.GetHeader(string)"/>.
        /// Comparison applies <see cref="NormalizeMessageId"/> (whitespace trim only) after
        /// <see cref="MessageIdValidation.IsValidMessageId(string?, bool)"/> with <c>stripSpaces: true</c>.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
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
        /// <param name="parsed">Parsed article supplying wire-order headers for date candidate scanning.</param>
        /// <param name="failureReason">
        /// When this method returns <see langword="false"/>, either <c>Required date header is missing or empty.</c> or
        /// <c>Date header is not parseable ({reason}).</c>; otherwise <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <see cref="ArticleDateHeaderResolver"/> resolves a canonical article date.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Passes <see cref="ParsedTransitArticle.OrderedHeaders"/> so the resolver can honor header precedence and
        /// document order. The resolved canonical instant is not persisted by this type.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
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
        /// Applies configured <see cref="PostFilterStyleOptions"/> shape checks and
        /// <see cref="NntpServerOptions.MaxArtSize"/> to the parsed article.
        /// </summary>
        /// <param name="parsed">Parsed article for header presence checks.</param>
        /// <param name="articleByteLength">Total article byte length including headers and body.</param>
        /// <param name="failureReason">
        /// When this method returns <see langword="false"/>, a size, forbidden-header, or crosspost limit message;
        /// otherwise <see langword="null"/>.
        /// </param>
        /// <returns><see langword="true"/> when all enabled style rules pass.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="NntpServerOptions.MaxArtSize"/> is enforced only when greater than zero — the same limit applied
        /// by transit command handlers and <see cref="NntpSpoolTransitStorage"/>.
        /// </para>
        /// <para>
        /// <see cref="PostFilterStyleOptions.ForbiddenHeaderNames"/> entries are trimmed and matched with
        /// case-insensitive <see cref="PostFilterParsedArticle.Headers"/> keys. Empty configured names are skipped.
        /// </para>
        /// <para>
        /// <see cref="PostFilterStyleOptions.MaxNewsgroupCrossposts"/> is enforced only when greater than zero. Group
        /// counting uses <see cref="CountNewsgroups"/> on the <c>Newsgroups</c> header when present.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        private bool TryValidateStyleRules(ParsedTransitArticle parsed, int articleByteLength, out string? failureReason)
        {
            failureReason = null;

            if (_serverOptions.MaxArtSize > 0 && articleByteLength > _serverOptions.MaxArtSize)
            {
                failureReason = $"Article exceeds configured maximum size ({_serverOptions.MaxArtSize} bytes).";
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
        /// <param name="newsgroups">Raw <c>Newsgroups</c> header value (may contain commas and whitespace).</param>
        /// <returns>
        /// Number of comma-separated tokens with non-whitespace content after trimming whitespace on each token span.
        /// </returns>
        /// <remarks>
        /// Empty tokens between commas are ignored. A trailing comma does not increment the count. Never throws.
        /// </remarks>
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
        /// Normalizes a Message-ID token for ordinal comparison after syntax validation.
        /// </summary>
        /// <param name="messageId">Candidate Message-ID that has already passed <see cref="MessageIdValidation"/>.</param>
        /// <returns>The input with leading and trailing whitespace removed.</returns>
        /// <remarks>
        /// Does not strip angle brackets or alter internal token text; <see cref="MessageIdValidation"/> handles syntax
        /// before this helper is applied. Never throws.
        /// </remarks>
        private static string NormalizeMessageId(string messageId)
        {
            return messageId.Trim();
        }
    }
}
