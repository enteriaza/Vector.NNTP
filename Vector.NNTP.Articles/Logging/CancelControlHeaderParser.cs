// <copyright file="CancelControlHeaderParser.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Control cancel target extraction for INN news log cancel lines.

using System.Text;
using Vector.NNTP.Articles.Classification;
using Vector.NNTP.Articles.Scanning;

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Parses the target Message-ID from a <c>Control: cancel</c> header for INN-style cancel log lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Consumed by <see cref="NntpNewsLog.LogCancelProcessed"/> after a cancel article is committed to the
    /// spool. The extracted token becomes the <c>Cancelling &lt;target&gt;</c> suffix via
    /// <see cref="NntpNewsLogFormatter.FormatCancelProcessed"/>. When parsing fails,
    /// <see cref="NntpNewsLog"/> substitutes <see cref="NntpNewsLogFeedNames.UnknownFeed"/> so the formatter emits
    /// <c>Cancelling ?</c> without angle brackets.
    /// </para>
    /// <para>
    /// <b>Caller bytes:</b> Production callers pass committed cancel article bytes after preprocessing and postprocessing
    /// (<c>postprocessResult.ArticleBytes</c> from <see cref="Storage.NntpSpoolWriterPump"/>), which may include local
    /// <c>Path</c> hop prepends unrelated to cancel target extraction.
    /// </para>
    /// <para><b>Extraction rules:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// Locate the header-body terminator with <see cref="ArticleByteScanSimd.FindHeaderEnd"/> and scan physical header
    /// lines in order from the start of the buffer through that boundary. RFC 5322 continuation folding is not applied;
    /// lines beginning with space or tab are separate physical lines and do not match <see cref="CancelPrefix"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Match a line whose field name and value prefix are <c>Control: cancel</c> using ASCII case-insensitive comparison
    /// via <see cref="HeaderValueUtilities.TryGetHeaderValue"/> and <see cref="CancelPrefix"/>. Other
    /// <c>Control:</c> verbs (for example <c>newgroup</c>) do not match. The prefix requires a space after
    /// <c>cancel</c>; targets glued to <c>cancel</c> without separating whitespace do not match.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Take the remainder after the prefix, apply <see cref="TrimAscii(ReadOnlySpan{byte})"/> (after any trim already
    /// performed by <see cref="HeaderValueUtilities.TryGetHeaderValue"/>), and decode the token with
    /// <see cref="Encoding.UTF8"/>. Angle brackets are preserved when present in the header value.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Return the first non-empty target and stop. When a matching <c>Control: cancel</c> line is found but the target
    /// is empty after trim, return <see langword="false"/> immediately <em>without</em> scanning later header lines
    /// (contrast <see cref="PathHeaderFeedResolver"/>, which continues after a rejected Path hop).
    /// </description>
    /// </item>
    /// </list>
    /// <para><b>Thread safety:</b> All members are static and stateless; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal static class CancelControlHeaderParser
    {
        /// <summary>
        /// Literal prefix bytes for a canonical <c>Control: cancel</c> header line, including the trailing space after
        /// <c>cancel</c>.
        /// </summary>
        /// <value>
        /// UTF-8 bytes for <c>Control: cancel </c> (field name, colon, space, <c>cancel</c>, trailing space). Evaluated
        /// through <see cref="ArticleByteScanSimd.StartsWithAsciiIgnoreCase"/> via
        /// <see cref="HeaderValueUtilities.TryGetHeaderValue"/>.
        /// </value>
        /// <remarks>
        /// <para>
        /// Passed to <see cref="HeaderValueUtilities.TryGetHeaderValue"/>, which performs ASCII case-insensitive prefix
        /// matching. A line beginning with <c>control: cancel &lt;target&gt;</c> therefore matches even though this
        /// literal uses mixed case.
        /// </para>
        /// <para>
        /// The trailing space is part of the prefix literal. Lines such as <c>Control: cancel&lt;target&gt;</c> (no space
        /// after <c>cancel</c>) do not match and are skipped without error.
        /// </para>
        /// </remarks>
        private static ReadOnlySpan<byte> CancelPrefix => "Control: cancel "u8;

        /// <summary>
        /// Attempts to read the cancelled Message-ID from article headers.
        /// </summary>
        /// <param name="articleBytes">
        /// Raw article bytes including headers and the header-body separator. May be empty when callers have no payload;
        /// empty input yields <see langword="false"/> without scanning.
        /// </param>
        /// <param name="targetMessageId">
        /// When this method returns <see langword="true"/>, the cancel target token decoded from the first usable
        /// <c>Control: cancel</c> header value (may include or omit angle brackets exactly as stored in the header).
        /// When this method returns <see langword="false"/>, set to <see cref="string.Empty"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a non-empty cancel target was extracted from a matching header line;
        /// <see langword="false"/> when <paramref name="articleBytes"/> is empty,
        /// <see cref="ArticleByteScanSimd.FindHeaderEnd"/> cannot locate a header-body boundary, no matching
        /// <c>Control: cancel</c> line is present, or the first matching line has an empty target after trim.
        /// </returns>
        /// <remarks>
        /// <para><b>Failure paths (all return <see langword="false"/> and leave <paramref name="targetMessageId"/> empty):</b></para>
        /// <list type="bullet">
        /// <item><description><paramref name="articleBytes"/> is empty.</description></item>
        /// <item><description>No header-body terminator (malformed article).</description></item>
        /// <item><description>No physical line matches <see cref="CancelPrefix"/>.</description></item>
        /// <item>
        /// <description>
        /// First matching <c>Control: cancel</c> line has an empty target after trim (scan stops; later lines are not
        /// considered).
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// <b>Success path:</b> Stops at the first matching line with a non-empty target; does not consider additional
        /// <c>Control: cancel</c> lines.
        /// </para>
        /// <para>
        /// Never throws for malformed input. Does not validate RFC 5322 Message-ID syntax beyond non-empty trim; malformed
        /// targets are passed through to <see cref="NntpNewsLogFormatter.FormatCancelProcessed"/>, which applies
        /// <see cref="NntpNewsLogFormatter.NormalizeMessageId"/> to the target on success.
        /// </para>
        /// <para>
        /// Scans only cancel-article headers for the <c>Control</c> target token, not the cancel article's own
        /// <c>Message-ID</c> field.
        /// </para>
        /// <para>
        /// Allocates a <see cref="string"/> only on success when decoding trimmed target bytes with <see cref="Encoding.UTF8"/>.
        /// </para>
        /// </remarks>
        /// <example>
        /// For <c>Control: cancel &lt;m070725@foo.com&gt;</c> on a line before the cancel article's
        /// <c>Message-ID</c>, this method returns <c>&lt;m070725@foo.com&gt;</c> with brackets preserved.
        /// </example>
        internal static bool TryParseCancelTarget(ReadOnlySpan<byte> articleBytes, out string targetMessageId)
        {
            targetMessageId = string.Empty;
            if (articleBytes.IsEmpty)
            {
                return false;
            }

            int headerEnd = ArticleByteScanSimd.FindHeaderEnd(articleBytes);
            if (headerEnd < 0)
            {
                return false;
            }

            int index = 0;
            while (index < headerEnd)
            {
                int lineEnd = ArticleByteScanSimd.IndexOfLineFeed(articleBytes, index, headerEnd);
                int contentEnd = lineEnd;
                if (contentEnd > index && articleBytes[contentEnd - 1] == '\r')
                {
                    contentEnd--;
                }

                ReadOnlySpan<byte> line = articleBytes[index..contentEnd];
                if (HeaderValueUtilities.TryGetHeaderValue(line, CancelPrefix, out ReadOnlySpan<byte> targetBytes))
                {
                    ReadOnlySpan<byte> trimmed = TrimAscii(targetBytes);
                    if (trimmed.IsEmpty)
                    {
                        return false;
                    }

                    targetMessageId = Encoding.UTF8.GetString(trimmed);
                    return true;
                }

                index = lineEnd + 1;
            }

            return false;
        }

        /// <summary>
        /// Trims ASCII horizontal whitespace from both ends of a byte span without allocating.
        /// </summary>
        /// <param name="value">
        /// Input bytes to trim, typically the cancel target slice after
        /// <see cref="HeaderValueUtilities.TryGetHeaderValue"/>.
        /// </param>
        /// <returns>
        /// A sub-span of <paramref name="value"/> with leading and trailing space (U+0020) and tab (U+0009) removed.
        /// Returns an empty span when <paramref name="value"/> is empty or contains only whitespace.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Applied after <see cref="HeaderValueUtilities.TryGetHeaderValue"/> so cancel targets with extra surrounding
        /// whitespace around internal content still decode cleanly. Does not trim other Unicode whitespace categories.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        private static ReadOnlySpan<byte> TrimAscii(ReadOnlySpan<byte> value)
        {
            int start = 0;
            while (start < value.Length && value[start] is (byte)' ' or (byte)'\t')
            {
                start++;
            }

            int end = value.Length;
            while (end > start && value[end - 1] is (byte)' ' or (byte)'\t')
            {
                end--;
            }

            return value[start..end];
        }
    }
}
