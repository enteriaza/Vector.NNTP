// <copyright file="HeaderValueUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: ASCII header value tokenization shared by article classification and postfilter crosspost checks.

using Vector.NNTP.Articles.Scanning;

namespace Vector.NNTP.Articles.Classification
{
    /// <summary>
    /// Shared ASCII helpers for NNTP header value parsing on raw unfolded header line bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Centralizes byte-level header value extraction and comparison for hot-path scanners that walk physical
    /// header lines without allocating decoded strings. Primary consumers are
    /// <see cref="ArticleTypeClassifier"/> (<c>Newsgroups:</c> / <c>Followup-To:</c> crosspost and redirect detection),
    /// <see cref="Logging.PathHeaderFeedResolver"/> (<c>Path:</c> feed token lookup), and
    /// <see cref="Logging.CancelControlHeaderParser"/> (<c>Control:</c> cancel target extraction).
    /// </para>
    /// <para>
    /// <b>Crosspost counting alignment:</b> <see cref="CountCommaSeparatedTokens"/> uses the same comma-splitting and
    /// empty-token skipping rules as <c>ArticleSpoolPostprocessor.CountNewsgroups</c> (comma-delimited spans with
    /// whitespace trimmed on each token). The postprocessor operates on decoded header strings with
    /// <see cref="string.Trim()"/> (Unicode whitespace categories), while this type trims only ASCII space, tab, CR, and
    /// LF on raw bytes. Results match for typical NNTP <c>Newsgroups:</c> values; they may diverge only when a token
    /// contains non-ASCII whitespace code points.
    /// </para>
    /// <para>
    /// <b>Folding:</b> Callers must pass unfolded physical header lines only. RFC 5322 continuation folding (lines
    /// beginning with horizontal whitespace) is not applied here.
    /// </para>
    /// <para><b>Thread safety:</b> Static and stateless; safe for concurrent spool writer pumps.</para>
    /// <para><b>Performance:</b> Allocation-free except <see cref="CopyHeaderValue"/>; prefix checks delegate to
    /// <see cref="ArticleByteScanSimd.StartsWithAsciiIgnoreCase"/>.</para>
    /// </remarks>
    internal static class HeaderValueUtilities
    {
        /// <summary>
        /// Counts non-empty comma-separated tokens in a header value byte span.
        /// </summary>
        /// <param name="value">
        /// Header value bytes after the field name and colon (may include leading whitespace). Typically the slice returned
        /// by <see cref="TryGetHeaderValue"/>.
        /// </param>
        /// <returns>
        /// Number of comma-separated tokens that contain at least one non-whitespace byte after ASCII trim. Returns
        /// <c>0</c> for an empty span or when every token is whitespace-only (for example <c>,,</c> or a value of only
        /// spaces).
        /// </returns>
        /// <remarks>
        /// <para><b>Tokenization rules:</b></para>
        /// <list type="bullet">
        /// <item><description>Commas delimit tokens; empty tokens between adjacent commas are ignored.</description></item>
        /// <item><description>A trailing comma does not add an extra token.</description></item>
        /// <item>
        /// <description>
        /// Each token span is trimmed with <see cref="TryTrimAscii"/> before the non-empty test (ASCII space, tab, CR,
        /// LF only).
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Used by <see cref="ArticleTypeClassifier"/> with <see cref="ArticleTypeClassifier.MassCrosspostThreshold"/> to
        /// set <see cref="ArticleTypeFlags.MassCrosspost"/>. Never throws for in-bounds spans.
        /// </para>
        /// </remarks>
        internal static int CountCommaSeparatedTokens(ReadOnlySpan<byte> value)
        {
            int count = 0;
            int index = 0;
            while (index <= value.Length)
            {
                int comma = value[index..].IndexOf((byte)',');
                int tokenEnd = comma < 0 ? value.Length : index + comma;
                if (TryTrimAscii(value, index, tokenEnd, out int trimStart, out int trimEnd) && trimEnd > trimStart)
                {
                    count++;
                }

                if (comma < 0)
                {
                    break;
                }

                index = tokenEnd + 1;
            }

            return count;
        }

        /// <summary>
        /// When a header line begins with a known field prefix, returns the value bytes after that prefix with ASCII trim
        /// applied.
        /// </summary>
        /// <param name="line">
        /// Full physical header line bytes without line terminators (no trailing <c>\r</c> or <c>\n</c>).
        /// </param>
        /// <param name="prefix">
        /// Expected field prefix bytes including the colon and any required following space (for example
        /// <c>Newsgroups:</c> or <c>Path: </c>). Matched with <see cref="ArticleByteScanSimd.StartsWithAsciiIgnoreCase"/>.
        /// </param>
        /// <param name="value">
        /// When this method returns <see langword="true"/>, the header value sub-span after <paramref name="prefix"/> with
        /// leading and trailing ASCII whitespace removed. When the suffix is whitespace-only, this is
        /// <see cref="ReadOnlySpan{T}.Empty"/>. When this method returns <see langword="false"/>, this is
        /// <see cref="ReadOnlySpan{T}.Empty"/> and must not be read.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="line"/> is at least as long as <paramref name="prefix"/> and begins
        /// with <paramref name="prefix"/> under ASCII case-insensitive comparison; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Prefix matching is delegated to <see cref="ArticleByteScanSimd.StartsWithAsciiIgnoreCase"/> (SIMD-accelerated on
        /// long prefixes). A line shorter than <paramref name="prefix"/> returns <see langword="false"/> immediately.
        /// </para>
        /// <para>
        /// <b>Prefix shape matters:</b> <see cref="Logging.PathHeaderFeedResolver"/> passes <c>Path: </c> (colon plus
        /// space); lines such as <c>Path:news.example.com</c> without that space do not match. Callers choose whether the
        /// prefix includes a mandatory space after the colon.
        /// </para>
        /// <para>
        /// On match, this method returns <see langword="true"/> even when the trimmed value is empty (for example
        /// <c>Followup-To:</c> with no token). Callers that require non-empty content must check
        /// <paramref name="value"/>.<see cref="ReadOnlySpan{T}.IsEmpty"/> after a successful return.
        /// </para>
        /// <para>Never throws for well-formed spans.</para>
        /// </remarks>
        internal static bool TryGetHeaderValue(
            ReadOnlySpan<byte> line,
            ReadOnlySpan<byte> prefix,
            out ReadOnlySpan<byte> value)
        {
            value = [];
            if (!ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, prefix))
            {
                return false;
            }

            ReadOnlySpan<byte> raw = line[prefix.Length..];
            if (TryTrimAscii(raw, 0, raw.Length, out int trimStart, out int trimEnd))
            {
                value = raw[trimStart..trimEnd];
            }

            return true;
        }

        /// <summary>
        /// Compares two header value byte spans for equality after ASCII trim and case-insensitive byte comparison.
        /// </summary>
        /// <param name="left">First header value bytes (often from <see cref="CopyHeaderValue"/> storage).</param>
        /// <param name="right">Second header value bytes to compare.</param>
        /// <returns>
        /// <see langword="true"/> when both spans represent the same logical token under ASCII trim and case folding;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para><b>Comparison rules:</b></para>
        /// <list type="number">
        /// <item>
        /// <description>
        /// Each span is trimmed with <see cref="TryTrimAscii"/> (ASCII space, tab, CR, LF). When both spans have zero
        /// length, returns <see langword="true"/>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// When either span trims to empty but the original span was not zero-length (whitespace-only content), returns
        /// <see langword="false"/> unless both originals are zero-length. Two whitespace-only spans therefore compare
        /// unequal.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Non-empty trimmed spans are compared byte-by-byte with <see cref="ArticleByteScanSimd.ToLowerAscii"/> applied
        /// to each byte (ASCII <c>A</c>–<c>Z</c> folding only; UTF-8 multibyte sequences are not normalized).
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Used by <see cref="ArticleTypeClassifier"/> at header completion to detect
        /// <see cref="ArticleTypeFlags.FollowupRedirect"/> when <c>Followup-To:</c> is present, non-empty, and differs
        /// from <c>Newsgroups:</c>.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        internal static bool EqualsAsciiIgnoreCaseTrimmed(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (!TryTrimAscii(left, 0, left.Length, out int leftStart, out int leftEnd) ||
                !TryTrimAscii(right, 0, right.Length, out int rightStart, out int rightEnd))
            {
                return left.IsEmpty && right.IsEmpty;
            }

            ReadOnlySpan<byte> leftTrimmed = left[leftStart..leftEnd];
            ReadOnlySpan<byte> rightTrimmed = right[rightStart..rightEnd];
            if (leftTrimmed.Length != rightTrimmed.Length)
            {
                return false;
            }

            for (int i = 0; i < leftTrimmed.Length; i++)
            {
                if (ArticleByteScanSimd.ToLowerAscii(leftTrimmed[i]) != ArticleByteScanSimd.ToLowerAscii(rightTrimmed[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Copies a header value byte span into a new heap array so it can outlive the current scan buffer.
        /// </summary>
        /// <param name="value">
        /// Header value bytes to copy (typically already trimmed by <see cref="TryGetHeaderValue"/>).
        /// </param>
        /// <returns>
        /// A new byte array containing exactly <paramref name="value"/>.Length bytes. Returns a zero-length array when
        /// <paramref name="value"/> is empty.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <see cref="ArticleTypeClassifier"/> stores <c>Newsgroups:</c> and <c>Followup-To:</c> values across multiple
        /// header lines and compares them in <see cref="EqualsAsciiIgnoreCaseTrimmed"/> after the header block ends; the
        /// copy prevents use-after-free when the underlying article buffer is reused.
        /// </para>
        /// <para>This is the only member in this type that allocates on the managed heap.</para>
        /// </remarks>
        internal static byte[] CopyHeaderValue(ReadOnlySpan<byte> value)
        {
            return value.ToArray();
        }

        /// <summary>
        /// Locates the trimmed sub-range of a byte span slice after removing ASCII leading and trailing whitespace.
        /// </summary>
        /// <param name="span">Source buffer containing the slice to trim.</param>
        /// <param name="start">Inclusive start index within <paramref name="span"/>.</param>
        /// <param name="endExclusive">Exclusive end index within <paramref name="span"/>.</param>
        /// <param name="trimStart">
        /// When this method returns <see langword="true"/>, the inclusive start offset of the trimmed content within
        /// <paramref name="span"/>. When this method returns <see langword="false"/>, equals <paramref name="start"/>.
        /// </param>
        /// <param name="trimEnd">
        /// When this method returns <see langword="true"/>, the exclusive end offset of the trimmed content within
        /// <paramref name="span"/>. When this method returns <see langword="false"/>, equals <paramref name="endExclusive"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the trimmed range contains at least one byte (<c>trimEnd &gt; trimStart</c>);
        /// <see langword="false"/> when the slice is empty or contains only bytes classified by <see cref="IsAsciiWhitespace"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Used internally by <see cref="CountCommaSeparatedTokens"/>, <see cref="TryGetHeaderValue"/>, and
        /// <see cref="EqualsAsciiIgnoreCaseTrimmed"/>. Does not validate that <paramref name="start"/> and
        /// <paramref name="endExclusive"/> lie within <paramref name="span"/>; callers must keep indices in range.
        /// </para>
        /// <para>Never throws when indices are in range.</para>
        /// </remarks>
        private static bool TryTrimAscii(ReadOnlySpan<byte> span, int start, int endExclusive, out int trimStart, out int trimEnd)
        {
            trimStart = start;
            trimEnd = endExclusive;
            while (trimStart < trimEnd && IsAsciiWhitespace(span[trimStart]))
            {
                trimStart++;
            }

            while (trimEnd > trimStart && IsAsciiWhitespace(span[trimEnd - 1]))
            {
                trimEnd--;
            }

            return trimEnd > trimStart;
        }

        /// <summary>
        /// Determines whether a byte is ASCII horizontal or vertical whitespace stripped by <see cref="TryTrimAscii"/>.
        /// </summary>
        /// <param name="value">Byte to test.</param>
        /// <returns>
        /// <see langword="true"/> for space (<c>U+0020</c>), tab (<c>U+0009</c>), carriage return (<c>U+000D</c>), or line
        /// feed (<c>U+000A</c>); otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Does not treat other Unicode whitespace (for example non-breaking space) as trimmable. Some callers such as
        /// <see cref="Logging.CancelControlHeaderParser"/> apply a narrower space/tab-only trim after
        /// <see cref="TryGetHeaderValue"/> for cancel targets.
        /// </para>
        /// </remarks>
        private static bool IsAsciiWhitespace(byte value)
        {
            return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
        }
    }
}
