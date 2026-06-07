// <copyright file="HeaderValueUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: ASCII header value tokenization shared by article classification and postfilter crosspost checks.

using Vector.NNTP.Articles.Scanning;

namespace Vector.NNTP.Articles.Classification
{
    /// <summary>
    /// Shared ASCII helpers for NNTP header value parsing on raw article byte lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors comma-token counting semantics used by
    /// <see cref="Processing.ArticleSpoolPostprocessor"/> crosspost validation so classification and rejection policy
    /// stay aligned on <c>Newsgroups:</c> shape.
    /// </para>
    /// <para><b>Limitation:</b> Operates on unfolded physical header lines only; RFC 5322 continuation folding is not applied.</para>
    /// </remarks>
    internal static class HeaderValueUtilities
    {
        /// <summary>
        /// Counts distinct non-empty comma-separated tokens in a header value span.
        /// </summary>
        /// <param name="value">Header value bytes after the field name and colon (may include leading whitespace).</param>
        /// <returns>Number of comma-separated tokens with non-whitespace content after ASCII trim.</returns>
        internal static int CountCommaSeparatedTokens(ReadOnlySpan<byte> value)
        {
            int count = 0;
            int index = 0;
            while (index <= value.Length)
            {
                int comma = value.Slice(index).IndexOf((byte)',');
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
        /// Returns the header value portion after a matched <paramref name="prefix"/> when the line starts with that prefix.
        /// </summary>
        /// <param name="line">Full header line bytes without line terminators.</param>
        /// <param name="prefix">Expected field prefix including trailing colon and optional space (for example <c>Newsgroups: </c>).</param>
        /// <param name="value">
        /// When this method returns <see langword="true"/>, the value slice after <paramref name="prefix"/> with ASCII trim applied.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="line"/> begins with <paramref name="prefix"/> under ASCII case-insensitive
        /// comparison; otherwise <see langword="false"/>.
        /// </returns>
        internal static bool TryGetHeaderValue(
            ReadOnlySpan<byte> line,
            ReadOnlySpan<byte> prefix,
            out ReadOnlySpan<byte> value)
        {
            value = ReadOnlySpan<byte>.Empty;
            if (!ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, prefix))
            {
                return false;
            }

            ReadOnlySpan<byte> raw = line[prefix.Length..];
            if (TryTrimAscii(raw, 0, raw.Length, out int trimStart, out int trimEnd))
            {
                value = raw.Slice(trimStart, trimEnd - trimStart);
            }

            return true;
        }

        /// <summary>
        /// Compares two header value spans for equality under ASCII case-insensitive trim semantics.
        /// </summary>
        /// <param name="left">First normalized header value.</param>
        /// <param name="right">Second normalized header value.</param>
        /// <returns><see langword="true"/> when both spans are equal after ASCII trim and case folding.</returns>
        internal static bool EqualsAsciiIgnoreCaseTrimmed(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (!TryTrimAscii(left, 0, left.Length, out int leftStart, out int leftEnd) ||
                !TryTrimAscii(right, 0, right.Length, out int rightStart, out int rightEnd))
            {
                return left.IsEmpty && right.IsEmpty;
            }

            ReadOnlySpan<byte> leftTrimmed = left.Slice(leftStart, leftEnd - leftStart);
            ReadOnlySpan<byte> rightTrimmed = right.Slice(rightStart, rightEnd - rightStart);
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
        /// Copies a header value span into a new byte array for deferred comparison.
        /// </summary>
        /// <param name="value">Trimmed header value bytes.</param>
        /// <returns>A byte array containing <paramref name="value"/>.</returns>
        internal static byte[] CopyHeaderValue(ReadOnlySpan<byte> value)
        {
            return value.ToArray();
        }

        /// <summary>
        /// Trims ASCII whitespace from both ends of a byte span slice.
        /// </summary>
        /// <param name="span">Source bytes.</param>
        /// <param name="start">Inclusive start index within <paramref name="span"/>.</param>
        /// <param name="endExclusive">Exclusive end index within <paramref name="span"/>.</param>
        /// <param name="trimStart">Inclusive trimmed start offset.</param>
        /// <param name="trimEnd">Exclusive trimmed end offset.</param>
        /// <returns><see langword="true"/> when the trimmed range is non-empty; otherwise <see langword="false"/>.</returns>
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
        /// Determines whether a byte is ASCII horizontal or vertical whitespace.
        /// </summary>
        /// <param name="value">Byte to test.</param>
        /// <returns><see langword="true"/> for space, tab, CR, or LF.</returns>
        private static bool IsAsciiWhitespace(byte value)
        {
            return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
        }
    }
}
