// <copyright file="MessageIdValidationSimd.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// MessageIdValidationSimd.cs -- Vectorized ASCII, whitespace trim, and atom-run scanning for Message-ID validation.
//
// Thread safety:
//   All methods are static and stateless. Safe for concurrent use from any thread.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Vector.NNTP.Utilities.Validation
{
    /// <summary>
    /// SIMD helpers for <see cref="MessageIdValidation"/> hot paths: ASCII pre-check, whitespace trim, and atom-character runs.
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — Vector256/Vector128 where hardware accelerated; scalar tail preserves semantics.</para>
    /// <para><b>Scope:</b> Message-ID tokens are bounded to 250 octets (RFC 3977); SIMD paths are tuned for that size class.</para>
    /// </remarks>
    internal static class MessageIdValidationSimd
    {
        /// <summary>
        /// Number of UTF-16 code units processed per 128-bit SIMD lane group.
        /// </summary>
        private const int Vector128CharCount = 8;

        /// <summary>
        /// Number of UTF-16 code units processed per 256-bit SIMD lane group.
        /// </summary>
        private const int Vector256CharCount = 16;

        /// <summary>
        /// Broadcast lower bound for decimal digit SIMD range checks.
        /// </summary>
        private static readonly Vector128<ushort> DigitLoVec128 = Vector128.Create((ushort)'0');

        /// <summary>
        /// Broadcast upper bound for decimal digit SIMD range checks.
        /// </summary>
        private static readonly Vector128<ushort> DigitHiVec128 = Vector128.Create((ushort)'9');

        /// <summary>
        /// Broadcast lower bound for uppercase ASCII letter SIMD range checks.
        /// </summary>
        private static readonly Vector128<ushort> UpperLoVec128 = Vector128.Create((ushort)'A');

        /// <summary>
        /// Broadcast upper bound for uppercase ASCII letter SIMD range checks.
        /// </summary>
        private static readonly Vector128<ushort> UpperHiVec128 = Vector128.Create((ushort)'Z');

        /// <summary>
        /// Broadcast lower bound for lowercase ASCII letter SIMD range checks.
        /// </summary>
        private static readonly Vector128<ushort> LowerLoVec128 = Vector128.Create((ushort)'a');

        /// <summary>
        /// Broadcast upper bound for lowercase ASCII letter SIMD range checks.
        /// </summary>
        private static readonly Vector128<ushort> LowerHiVec128 = Vector128.Create((ushort)'z');

        /// <summary>
        /// Broadcast ASCII space for whitespace SIMD scans.
        /// </summary>
        private static readonly Vector128<ushort> SpaceVec128 = Vector128.Create((ushort)' ');

        /// <summary>
        /// Broadcast ASCII horizontal tab for whitespace SIMD scans.
        /// </summary>
        private static readonly Vector128<ushort> TabVec128 = Vector128.Create((ushort)'\t');

        /// <summary>
        /// Returns the index of the first non-whitespace character in <paramref name="span"/>[<paramref name="start"/>..<paramref name="end"/>).
        /// </summary>
        /// <param name="span">Source span.</param>
        /// <param name="start">Inclusive scan start.</param>
        /// <param name="end">Exclusive scan end.</param>
        /// <returns>Index of the first non-whitespace character, or <paramref name="end"/> when the range is all whitespace.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int TrimLeadingWhitespace(ReadOnlySpan<char> span, int start, int end)
        {
            int i = start;
            if (Vector128.IsHardwareAccelerated)
            {
                ref ushort searchRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span));
                int simdEnd = end - Vector128CharCount;
                while (i <= simdEnd)
                {
                    Vector128<ushort> chunk = Vector128.LoadUnsafe(ref searchRef, (nuint)i);
                    Vector128<ushort> isWhitespace = Vector128.BitwiseOr(
                        Vector128.Equals(chunk, SpaceVec128),
                        Vector128.Equals(chunk, TabVec128));
                    uint mask = Vector128.ExtractMostSignificantBits(isWhitespace);
                    if (mask != 0xFF)
                    {
                        uint notWhitespace = (~mask) & 0xFF;
                        return i + BitOperations.TrailingZeroCount(notWhitespace);
                    }

                    i += Vector128CharCount;
                }
            }

            while (i < end && IsWhitespace(span[i]))
            {
                i++;
            }

            return i;
        }

        /// <summary>
        /// Returns the exclusive end index after trimming trailing ASCII whitespace from a bounded range.
        /// </summary>
        /// <param name="span">Source span.</param>
        /// <param name="start">Inclusive range start (first content index).</param>
        /// <param name="end">Exclusive range end before trimming.</param>
        /// <returns>Exclusive end index with trailing space/tab removed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int TrimTrailingWhitespace(ReadOnlySpan<char> span, int start, int end)
        {
            int i = end;
            while (i > start && IsWhitespace(span[i - 1]))
            {
                i--;
            }

            return i;
        }

        /// <summary>
        /// Returns <see langword="true"/> when every UTF-16 code unit in the bounded range is US-ASCII (high byte zero).
        /// </summary>
        /// <param name="span">Source span.</param>
        /// <param name="start">Inclusive range start.</param>
        /// <param name="end">Exclusive range end.</param>
        /// <returns><see langword="true"/> when the range is empty or all code units are ASCII.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsAllAscii(ReadOnlySpan<char> span, int start, int end)
        {
            int i = start;
            if (i >= end)
            {
                return true;
            }

            ref ushort searchRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span));

            if (Vector256.IsHardwareAccelerated)
            {
                Vector256<ushort> zero = Vector256<ushort>.Zero;
                int simdEnd = end - Vector256CharCount;
                while (i <= simdEnd)
                {
                    Vector256<ushort> chunk = Vector256.LoadUnsafe(ref searchRef, (nuint)i);
                    if (!Vector256.EqualsAll(Vector256.ShiftRightLogical(chunk, 8), zero))
                    {
                        return false;
                    }

                    i += Vector256CharCount;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                Vector128<ushort> zero = Vector128<ushort>.Zero;
                int simdEnd = end - Vector128CharCount;
                while (i <= simdEnd)
                {
                    Vector128<ushort> chunk = Vector128.LoadUnsafe(ref searchRef, (nuint)i);
                    if (!Vector128.EqualsAll(Vector128.ShiftRightLogical(chunk, 8), zero))
                    {
                        return false;
                    }

                    i += Vector128CharCount;
                }
            }

            for (; i < end; i++)
            {
                if (span[i] > 127)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Consumes a maximal prefix of Message-ID atom characters beginning at <paramref name="start"/>.
        /// </summary>
        /// <param name="span">Source span.</param>
        /// <param name="start">Inclusive start index of the atom run.</param>
        /// <param name="end">Exclusive parse bound.</param>
        /// <returns>Number of atom characters consumed (zero when <paramref name="start"/> is not an atom character).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int ConsumeAtomCharacters(ReadOnlySpan<char> span, int start, int end)
        {
            int i = start;
            while (i < end)
            {
                int alnumRun = ConsumeAlphanumericPrefix(span, i, end);
                if (alnumRun > 0)
                {
                    i += alnumRun;
                    continue;
                }

                if (!MessageIdCharClasses.IsAtom(span[i]))
                {
                    break;
                }

                i++;
            }

            return i - start;
        }

        /// <summary>
        /// Consumes a maximal prefix of ASCII letters and digits beginning at <paramref name="start"/>.
        /// </summary>
        /// <param name="span">Source span.</param>
        /// <param name="start">Inclusive start index.</param>
        /// <param name="end">Exclusive parse bound.</param>
        /// <returns>Number of alphanumeric characters consumed from <paramref name="start"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int ConsumeAlphanumericPrefix(ReadOnlySpan<char> span, int start, int end)
        {
            int i = start;
            if (!Vector128.IsHardwareAccelerated)
            {
                while (i < end && IsAsciiLetterOrDigit(span[i]))
                {
                    i++;
                }

                return i - start;
            }

            ref ushort searchRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span));
            Vector128<ushort> zero = Vector128<ushort>.Zero;
            int simdEnd = end - Vector128CharCount;
            while (i <= simdEnd)
            {
                Vector128<ushort> chunk = Vector128.LoadUnsafe(ref searchRef, (nuint)i);
                if (!Vector128.EqualsAll(Vector128.ShiftRightLogical(chunk, 8), zero))
                {
                    break;
                }

                Vector128<ushort> isDigit = Vector128.BitwiseAnd(
                    Vector128.GreaterThanOrEqual(chunk, DigitLoVec128),
                    Vector128.LessThanOrEqual(chunk, DigitHiVec128));
                Vector128<ushort> isUpper = Vector128.BitwiseAnd(
                    Vector128.GreaterThanOrEqual(chunk, UpperLoVec128),
                    Vector128.LessThanOrEqual(chunk, UpperHiVec128));
                Vector128<ushort> isLower = Vector128.BitwiseAnd(
                    Vector128.GreaterThanOrEqual(chunk, LowerLoVec128),
                    Vector128.LessThanOrEqual(chunk, LowerHiVec128));
                Vector128<ushort> isAlnum = Vector128.BitwiseOr(
                    Vector128.BitwiseOr(isDigit, isUpper),
                    isLower);

                uint mask = Vector128.ExtractMostSignificantBits(isAlnum);
                if (mask != 0xFF)
                {
                    uint notAlnum = (~mask) & 0xFF;
                    return i - start + BitOperations.TrailingZeroCount(notAlnum);
                }

                i += Vector128CharCount;
            }

            while (i < end && IsAsciiLetterOrDigit(span[i]))
            {
                i++;
            }

            return i - start;
        }

        /// <summary>
        /// Returns whether <paramref name="c"/> is ASCII space or horizontal tab.
        /// </summary>
        /// <param name="c">Character to test.</param>
        /// <returns><see langword="true"/> for space or tab.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhitespace(char c)
        {
            return c is ' ' or '\t';
        }

        /// <summary>
        /// Returns whether <paramref name="c"/> is an ASCII letter or digit without calling BCL classification helpers.
        /// </summary>
        /// <param name="c">Character to test.</param>
        /// <returns><see langword="true"/> when the character is in <c>A-Z</c>, <c>a-z</c>, or <c>0-9</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAsciiLetterOrDigit(char c)
        {
            uint u = c;
            return (u - '0' <= 9) | (u - 'A' <= 25) | (u - 'a' <= 25);
        }
    }
}
