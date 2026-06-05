// <copyright file="AsciiSpanUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// AsciiSpanUtilities.cs -- Shared, zero-allocation ASCII byte-span helpers for whitespace trimming, searching,
// numeric parsing, and line-ending trimming.

using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Parsing
{
    /// <summary>
    /// Zero-allocation ASCII byte-span helpers for whitespace trimming, whitespace searching, numeric parsing,
    /// line-ending stripping, digit counting, and digit-only validation.
    /// </summary>
    /// <remarks>
    /// <para><b>Whitespace scope:</b> Only ASCII space (0x20) and horizontal tab (0x09) are considered whitespace. This
    /// matches NNTP's definition of in-line whitespace (RFC 3977 Â§3.1) and avoids the broader Unicode definition of
    /// <see cref="char.IsWhiteSpace(char)"/>.</para>
    ///
    /// <para><b>Allocation:</b> All methods operate on <see cref="ReadOnlySpan{T}"/> and return spans or primitives,
    /// allocating nothing on the heap.</para>
    /// </remarks>
    public static class AsciiSpanUtilities
    {
        /// <summary>
        /// ASCII space byte (0x20).
        /// </summary>
        public const byte Space = (byte)' ';

        /// <summary>
        /// ASCII horizontal tab byte (0x09).
        /// </summary>
        public const byte Tab = (byte)'\t';

        /// <summary>
        /// ASCII carriage return byte (0x0D).
        /// </summary>
        private const byte CR = (byte)'\r';

        /// <summary>
        /// ASCII line feed byte (0x0A).
        /// </summary>
        private const byte LF = (byte)'\n';

        /// <summary>
        /// ASCII digit 0 byte (0x30).
        /// </summary>
        private const byte Digit0 = (byte)'0';

        /// <summary>
        /// ASCII digit 9 byte (0x39).
        /// </summary>
        private const byte Digit9 = (byte)'9';

        /// <summary>
        /// Trims leading and trailing ASCII whitespace (space and tab) from a byte span without allocation.
        /// </summary>
        /// <param name="span">The span to trim.</param>
        /// <returns>A sub-span with leading and trailing whitespace removed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> span)
        {
            int start = 0;
            while (start < span.Length && IsAsciiWhitespace(span[start]))
            {
                start++;
            }

            int end = span.Length - 1;
            while (end >= start && IsAsciiWhitespace(span[end]))
            {
                end--;
            }

            return span[start..(end + 1)];
        }

        /// <summary>
        /// Trims leading ASCII whitespace (space and tab) from a byte span without allocation.
        /// </summary>
        /// <param name="span">The span to trim.</param>
        /// <returns>A sub-span with leading whitespace removed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<byte> TrimLeadingAsciiWhitespace(ReadOnlySpan<byte> span)
        {
            int start = 0;
            while (start < span.Length && IsAsciiWhitespace(span[start]))
            {
                start++;
            }

            return span[start..];
        }

        /// <summary>
        /// Returns the index of the first ASCII whitespace byte (space or tab), or -1 if none is found.
        /// </summary>
        /// <param name="span">The span to search.</param>
        /// <returns>The zero-based index of the first whitespace byte, or -1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfAsciiWhitespace(ReadOnlySpan<byte> span)
        {
            return span.IndexOfAny(Space, Tab);
        }

        /// <summary>
        /// Returns <see langword="true"/> if the byte is ASCII whitespace (space or horizontal tab).
        /// </summary>
        /// <param name="b">The byte to test.</param>
        /// <returns><see langword="true"/> if whitespace.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiWhitespace(byte b)
        {
            return b is Space or Tab;
        }

        /// <summary>
        /// Strips a trailing CRLF (\r\n) or bare LF (\n) from the end of a byte span.
        /// </summary>
        /// <param name="span">Raw line bytes.</param>
        /// <returns>A sub-span without the line ending.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<byte> TrimLineEnding(ReadOnlySpan<byte> span)
        {
            return span.Length >= 2 && span[^2] == CR && span[^1] == LF ? span[..^2] : span.Length >= 1 && span[^1] == LF ? span[..^1] : span;
        }

        /// <summary>
        /// Parses a non-negative decimal integer from an ASCII byte span.
        /// </summary>
        /// <param name="span">ASCII digits.</param>
        /// <param name="value">Parsed value on success; 0 on failure.</param>
        /// <returns><see langword="true"/> if parse succeeded; otherwise <see langword="false"/>.</returns>
        public static bool TryParseAsciiInt64(ReadOnlySpan<byte> span, out long value)
        {
            value = 0;

            if (span.IsEmpty)
            {
                return false;
            }

            long current = 0;

            for (int i = 0; i < span.Length; i++)
            {
                byte b = span[i];
                uint digit = (uint)(b - Digit0);

                if (digit > 9)
                {
                    value = 0;
                    return false;
                }

                if (current > (long.MaxValue - digit) / 10)
                {
                    value = 0;
                    return false;
                }

                current = (current * 10) + digit;
            }

            value = current;
            return true;
        }

        /// <summary>
        /// Returns <see langword="true"/> if every byte in <paramref name="span"/> is an ASCII digit (0-9).
        /// </summary>
        /// <param name="span">Bytes to validate.</param>
        /// <returns><see langword="true"/> if all bytes are digits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsOnlyAsciiDigits(ReadOnlySpan<byte> span)
        {
            return span.IndexOfAnyExceptInRange(Digit0, Digit9) < 0;
        }
    }
}
