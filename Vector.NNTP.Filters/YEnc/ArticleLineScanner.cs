// <copyright file="ArticleLineScanner.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// ArticleLineScanner.cs -- Byte-level CRLF and line-prefix scanning for raw NNTP article body spans (used by yEnc validation).
//
// Thread safety:
//   All methods are static and stateless.

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Vector.NNTP.Filters.YEnc
{
    /// <summary>
    /// Byte-level CRLF and line-prefix scanning for raw NNTP article body spans (used by yEnc validation).
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — uses Vector128 when available; scalar tail preserves semantics.</para>
    /// </remarks>
    internal static class ArticleLineScanner
    {
        /// <summary>Carriage return byte; paired with <see cref="LF"/> for CRLF line endings.</summary>
        private const byte CR = (byte)'\r';

        /// <summary>Line feed byte; may appear alone as a line terminator on some wire paths.</summary>
        private const byte LF = (byte)'\n';

        /// <summary>
        /// Shuffle indices to build <c>span[i+j-1]</c> for <c>j = 1..15</c> and zero lane 0 (used when <c>i == startOffset</c>).
        /// </summary>
        private static readonly Vector128<byte> PrevByteShuffleIndices = Vector128.Create(
            0xFF, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);

        /// <summary>
        /// Finds the first line terminator at or after <paramref name="startOffset"/>:
        /// <c>CR LF</c> (returns the index of <c>CR</c>), else a standalone <c>LF</c> (LF-only wire format).
        /// </summary>
        /// <remarks>
        /// yEnc lines can contain a raw <c>CR</c> byte in the encoded payload (decoded value 227); only
        /// <c>CR LF</c> pairs delimit NNTP-style lines, so a bare <c>CR</c> must not end a line.
        /// </remarks>
        /// <param name="span">Article body span.</param>
        /// <param name="startOffset">Start offset to search from.</param>
        /// <returns>Index of the first terminator byte, or -1 when none exists.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfCrLf(ReadOnlySpan<byte> span, int startOffset)
        {
            if ((uint)startOffset >= (uint)span.Length)
            {
                return -1;
            }

            int i = startOffset;
            int n = span.Length;
            ref byte b = ref MemoryMarshal.GetReference(span);

            if (Vector128.IsHardwareAccelerated && i + 17 <= n)
            {
                Vector128<byte> crVec = Vector128.Create(CR);
                Vector128<byte> lfVec = Vector128.Create(LF);
                Vector128<byte> allOnes = Vector128<byte>.AllBitsSet;

                while (i + 17 <= n)
                {
                    Vector128<byte> v0 = Vector128.LoadUnsafe(ref b, (nuint)i);
                    Vector128<byte> v1 = Vector128.LoadUnsafe(ref b, (nuint)(i + 1));
                    Vector128<byte> crlf = Vector128.BitwiseAnd(
                        Vector128.Equals(v0, crVec),
                        Vector128.Equals(v1, lfVec));

                    Vector128<byte> prevBytes = i > startOffset
                        ? Vector128.LoadUnsafe(ref b, (nuint)(i - 1))
                        : Vector128.Shuffle(v0, PrevByteShuffleIndices);

                    int relStart = startOffset - i;
                    Vector128<byte> atStart = Vector128<byte>.Zero;
                    if ((uint)relStart < 16)
                    {
                        atStart = Vector128.WithElement(atStart, relStart, (byte)0xFF);
                    }

                    Vector128<byte> lf = Vector128.Equals(v0, lfVec);
                    Vector128<byte> prevNotCr = Vector128.AndNot(allOnes, Vector128.Equals(prevBytes, crVec));
                    Vector128<byte> standaloneLf = Vector128.BitwiseAnd(
                        lf,
                        Vector128.BitwiseOr(atStart, prevNotCr));

                    Vector128<byte> hit = Vector128.BitwiseOr(crlf, standaloneLf);
                    uint bits = Vector128.ExtractMostSignificantBits(hit);
                    if (bits != 0)
                    {
                        return i + BitOperations.TrailingZeroCount(bits);
                    }

                    i += 16;
                }
            }

            return IndexOfCrLfScalar(ref b, n, i, startOffset);
        }

        /// <summary>
        /// Scalar tail and non-accelerated targets; preserves semantics of <see cref="IndexOfCrLf"/>.
        /// </summary>
        /// <param name="b">Reference to the start of the span.</param>
        /// <param name="n">Span length.</param>
        /// <param name="i">Start index.</param>
        /// <param name="startOffset">Initial start offset.</param>
        /// <returns>Index of terminator byte, or -1.</returns>
        private static int IndexOfCrLfScalar(ref byte b, int n, int i, int startOffset)
        {
            for (; i < n; i++)
            {
                if (Unsafe.Add(ref b, (nint)(uint)i) == CR && i + 1 < n && Unsafe.Add(ref b, (nint)(uint)(i + 1)) == LF)
                {
                    return i;
                }

                if (Unsafe.Add(ref b, (nint)(uint)i) == LF && (i == startOffset || Unsafe.Add(ref b, (nint)(uint)(i - 1)) != CR))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns the index of the first byte after the line terminator beginning at <paramref name="lineEndIndex"/>.
        /// </summary>
        /// <param name="span">Body span.</param>
        /// <param name="lineEndIndex">Index returned by <see cref="IndexOfCrLf"/>.</param>
        /// <returns>Index after CRLF or LF terminator; or <c>span.Length</c> when no terminator exists.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AdvancePastLineTerminator(ReadOnlySpan<byte> span, int lineEndIndex)
        {
            if (lineEndIndex < 0)
            {
                return span.Length;
            }

            if (span[lineEndIndex] == CR && lineEndIndex + 1 < span.Length && span[lineEndIndex + 1] == LF)
            {
                return lineEndIndex + 2;
            }

            return lineEndIndex + 1;
        }

        /// <summary>
        /// Finds the start index of the first line at or after <paramref name="startOffset"/> that begins with <paramref name="prefix"/>.
        /// </summary>
        /// <param name="span">Body span.</param>
        /// <param name="startOffset">Start offset.</param>
        /// <param name="prefix">Line prefix to match.</param>
        /// <returns>Start index of the matching line, or -1.</returns>
        public static int FindLineStartingWith(ReadOnlySpan<byte> span, int startOffset, ReadOnlySpan<byte> prefix)
        {
            int lineStart = startOffset;

            while (lineStart < span.Length)
            {
                if (span[lineStart..].StartsWith(prefix))
                {
                    return lineStart;
                }

                int crlfIndex = IndexOfCrLf(span, lineStart);

                if (crlfIndex < 0)
                {
                    break;
                }

                lineStart = AdvancePastLineTerminator(span, crlfIndex);
            }

            return -1;
        }
    }
}

