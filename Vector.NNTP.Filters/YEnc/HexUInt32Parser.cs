// <copyright file="HexUInt32Parser.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// HexUInt32Parser.cs -- Variable-length hexadecimal parsing for wire bytes (e.g. yEnc crc32= / pcrc32= fields).
//
// Thread safety:
//   All methods are static and stateless.

namespace Vector.NNTP.Filters.YEnc
{
    /// <summary>
    /// Variable-length hexadecimal parsing for wire bytes (e.g. yEnc <c>crc32=</c> / <c>pcrc32=</c> fields).
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — scalar loop with early-exit on first non-hex byte.</para>
    /// </remarks>
    internal static class HexUInt32Parser
    {
        /// <summary>
        /// Converts a single ASCII hex byte to its 4-bit nibble value, or -1 if invalid.
        /// </summary>
        /// <param name="b">ASCII byte to convert.</param>
        /// <returns>Nibble value in [0,15] on success; -1 when invalid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int HexByteToNibble(byte b)
        {
            if ((uint)(b - (byte)'0') <= 9)
            {
                return b - (byte)'0';
            }

            if ((uint)(b - (byte)'a') <= 5)
            {
                return b - (byte)'a' + 10;
            }

            if ((uint)(b - (byte)'A') <= 5)
            {
                return b - (byte)'A' + 10;
            }

            return -1;
        }

        /// <summary>
        /// Parses a variable-length hexadecimal byte sequence into a <see cref="uint"/>.
        /// </summary>
        /// <param name="hexBytes">Hex ASCII bytes; parsing stops at the first non-hex byte.</param>
        /// <param name="value">Parsed value when the method returns <see langword="true"/>.</param>
        /// <returns><see langword="true"/> if at least one hex digit was parsed.</returns>
        internal static bool TryParseHexUInt32(ReadOnlySpan<byte> hexBytes, out uint value)
        {
            value = 0;
            bool hasDigits = false;

            for (int i = 0; i < hexBytes.Length; i++)
            {
                int nibble = HexByteToNibble(hexBytes[i]);

                if (nibble < 0)
                {
                    break;
                }

                value = (value << 4) | (uint)nibble;
                hasDigits = true;
            }

            return hasDigits;
        }
    }
}

