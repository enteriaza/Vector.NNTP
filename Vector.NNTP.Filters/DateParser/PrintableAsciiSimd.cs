// <copyright file="PrintableAsciiSimd.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// PrintableAsciiSimd.cs -- SIMD printable ASCII range check for date quick-parse paths.
//
// Thread safety:
//   All methods are static and stateless.

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Vectorized check that every UTF-16 code unit in a span lies in the printable ASCII range (U+0020 through U+007E).
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b> Used by <see cref="NewsDateParser"/> quick-parse to reject obviously non-ASCII garbage before calling
    /// <see cref="DateTimeOffset.TryParse(ReadOnlySpan{char}, IFormatProvider?, DateTimeStyles, out DateTimeOffset)"/>.</para>
    ///
    /// <para><b>Performance:</b> HOT PATH — Vector256/Vector128 where available; scalar tail for remainder.</para>
    /// </remarks>
    internal static class PrintableAsciiSimd
    {
        /// <summary>Number of <see cref="ushort"/> lanes in a 128-bit vector used for ASCII scanning.</summary>
        private const int Vector128UShortCount = 8;

        /// <summary>Number of <see cref="ushort"/> lanes in a 256-bit vector used for ASCII scanning.</summary>
        private const int Vector256UShortCount = 16;

        /// <summary>Lower bound (inclusive) broadcast for 256-bit printable ASCII checks.</summary>
        private static readonly Vector256<ushort> PrintableLoVec256 = Vector256.Create((ushort)0x20);

        /// <summary>Upper range width broadcast for 256-bit printable ASCII checks (inclusive upper = lo + range).</summary>
        private static readonly Vector256<ushort> PrintableRangeVec256 = Vector256.Create((ushort)0x5E);

        /// <summary>Lower bound (inclusive) broadcast for 128-bit printable ASCII checks.</summary>
        private static readonly Vector128<ushort> PrintableLoVec128 = Vector128.Create((ushort)0x20);

        /// <summary>Upper range width broadcast for 128-bit printable ASCII checks.</summary>
        private static readonly Vector128<ushort> PrintableRangeVec128 = Vector128.Create((ushort)0x5E);

        /// <summary>
        /// Returns <see langword="true"/> when every character in <paramref name="span"/> is printable ASCII (U+0020–U+007E), or when <paramref name="span"/> is empty.
        /// </summary>
        /// <param name="span">The text to validate.</param>
        /// <returns><see langword="true"/> if the span is empty or all code units are in range; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAllPrintableAscii(ReadOnlySpan<char> span)
        {
            return span.Length == 0 || IsAllInRange(span, PrintableLoVec256, PrintableRangeVec256, PrintableLoVec128, PrintableRangeVec128, scalarLo: 0x20, scalarRange: 0x5E);
        }

        /// <summary>
        /// SIMD plus scalar tail: verifies every UTF-16 unit is within <paramref name="scalarLo"/>..(<paramref name="scalarLo"/>+<paramref name="scalarRange"/>).
        /// </summary>
        /// <param name="span">Non-empty character span to scan.</param>
        /// <param name="loVec256">256-bit lower bound broadcast for the accelerated path.</param>
        /// <param name="rangeVec256">256-bit inclusive range width broadcast.</param>
        /// <param name="loVec128">128-bit lower bound broadcast.</param>
        /// <param name="rangeVec128">128-bit inclusive range width broadcast.</param>
        /// <param name="scalarLo">Scalar lower bound for the tail loop.</param>
        /// <param name="scalarRange">Scalar inclusive range width for the tail loop.</param>
        /// <returns><see langword="true"/> when every code unit is in range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAllInRange(
            ReadOnlySpan<char> span,
            Vector256<ushort> loVec256,
            Vector256<ushort> rangeVec256,
            Vector128<ushort> loVec128,
            Vector128<ushort> rangeVec128,
            ushort scalarLo,
            ushort scalarRange)
        {
            Debug.Assert(span.Length > 0, "IsAllInRange requires non-empty span.");

            int i = 0;
            ref ushort searchRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span));

            if (Vector256.IsHardwareAccelerated)
            {
                int simd256End = span.Length - Vector256UShortCount;
                while (i <= simd256End)
                {
                    Vector256<ushort> chunk = Vector256.LoadUnsafe(ref searchRef, (nuint)i);
                    Vector256<ushort> adjusted = Vector256.Subtract(chunk, loVec256);
                    Vector256<ushort> outOfRange = Vector256.GreaterThan(adjusted, rangeVec256);
                    if (!Vector256.EqualsAll(outOfRange, Vector256<ushort>.Zero))
                    {
                        return false;
                    }

                    i += Vector256UShortCount;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                int simd128End = span.Length - Vector128UShortCount;
                while (i <= simd128End)
                {
                    Vector128<ushort> chunk = Vector128.LoadUnsafe(ref searchRef, (nuint)i);
                    Vector128<ushort> adjusted = Vector128.Subtract(chunk, loVec128);
                    Vector128<ushort> outOfRange = Vector128.GreaterThan(adjusted, rangeVec128);
                    if (!Vector128.EqualsAll(outOfRange, Vector128<ushort>.Zero))
                    {
                        return false;
                    }

                    i += Vector128UShortCount;
                }
            }

            for (; i < span.Length; i++)
            {
                if ((uint)(span[i] - scalarLo) > scalarRange)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

