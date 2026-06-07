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
    /// Consumed by <see cref="NntpNewsLog.LogCancelProcessed"/> when a cancel article is committed to the spool. The
    /// extracted token is passed to <see cref="NntpNewsLogFormatter.FormatCancelProcessed"/> as the
    /// <c>Cancelling &lt;target&gt;</c> suffix on the cancel line. When parsing fails,
    /// <see cref="NntpNewsLog"/> substitutes <see cref="NntpNewsLogFeedNames.UnknownFeed"/> so the formatter emits
    /// <c>Cancelling ?</c>.
    /// </para>
    /// <para><b>Extraction rules:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// Scan physical header lines in order from the start of the article through the header-body blank line located by
    /// <see cref="ArticleByteScanSimd.FindHeaderEnd"/>. RFC 5322 continuation folding is not applied; each line is
    /// evaluated independently.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Match a line whose field name and value prefix are <c>Control: cancel</c> using ASCII case-insensitive comparison
    /// via <see cref="HeaderValueUtilities.TryGetHeaderValue"/> and <see cref="CancelPrefix"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Take the remainder of the line after the prefix, trim ASCII space and tab from both ends, and decode the token with
    /// <see cref="Encoding.UTF8"/>. Angle brackets are preserved when present in the header value.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Return the first non-empty target. When a matching <c>Control: cancel</c> line is found but the target token is
    /// empty after trim, this method returns <see langword="false"/> immediately without scanning later header lines.
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
        /// <remarks>
        /// <para>
        /// Passed to <see cref="HeaderValueUtilities.TryGetHeaderValue"/>, which performs ASCII case-insensitive prefix
        /// matching. A line beginning with <c>control: cancel &lt;target&gt;</c> therefore matches even though this constant
        /// uses mixed case.
        /// </para>
        /// <para>
        /// The trailing space is required by the prefix literal; values immediately adjacent to <c>cancel</c> without
        /// separating whitespace do not match this prefix.
        /// </para>
        /// </remarks>
        private static ReadOnlySpan<byte> CancelPrefix => "Control: cancel "u8;

        /// <summary>
        /// Attempts to read the cancelled Message-ID from article headers.
        /// </summary>
        /// <param name="articleBytes">
        /// Raw article bytes including headers and the header-body separator. May be empty; empty input yields
        /// <see langword="false"/> without scanning.
        /// </param>
        /// <param name="targetMessageId">
        /// When this method returns <see langword="true"/>, the cancel target token decoded from the first usable
        /// <c>Control: cancel</c> header value, suitable for
        /// <see cref="NntpNewsLogFormatter.FormatCancelProcessed"/> (may include or omit angle brackets exactly as stored in
        /// the header). When this method returns <see langword="false"/>, set to <see cref="string.Empty"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a non-empty cancel target was extracted from a matching header line;
        /// <see langword="false"/> when <paramref name="articleBytes"/> is empty, no header-body boundary exists, no
        /// matching <c>Control: cancel</c> line is present, or the first matching line has an empty target after trim.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Never throws. Does not validate Message-ID syntax beyond non-empty trim; malformed targets are passed through to
        /// the formatter, which applies <see cref="NntpNewsLogFormatter.NormalizeMessageId"/> when building the log line.
        /// </para>
        /// <para>
        /// Only the cancel article headers are scanned; the target is the Message-ID named in the
        /// <c>Control</c> header, not the cancel article's own <c>Message-ID</c> field.
        /// </para>
        /// </remarks>
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
        /// <param name="value">Input bytes to trim, typically the cancel target slice after prefix matching.</param>
        /// <returns>
        /// A sub-span of <paramref name="value"/> with leading and trailing space (U+0020) and tab (U+0009) removed.
        /// Returns an empty span when <paramref name="value"/> is empty or contains only whitespace.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Applied after <see cref="HeaderValueUtilities.TryGetHeaderValue"/> so cancel targets with extra surrounding
        /// whitespace still decode cleanly. Does not trim other Unicode whitespace categories.
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
