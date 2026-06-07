// <copyright file="MessageIdCharClasses.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// MessageIdCharClasses.cs -- O(1) bitmap character-class lookup for Message-ID atom and no-fold-literal grammar.

using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Validation
{
    /// <summary>
    /// Bitmap character-class lookup for INN <c>messageid.c</c> atom and no-fold-literal (<c>mdtext</c>) rules.
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — each test is one bounds check, one <see cref="uint"/> load, and one bit test;
    /// no branches beyond the high-bit reject.</para>
    /// <para><b>Thread safety:</b> Static bitmap tables are initialized before first use; safe for concurrent reads.</para>
    /// </remarks>
    internal static class MessageIdCharClasses
    {
        /// <summary>
        /// Number of <see cref="uint"/> words in each 256-bit character-class bitmap.
        /// </summary>
        private const int BitmapWordCount = 8;

        /// <summary>
        /// Atom-character bitmap (RFC 5536 mdtext atom subset used on the Message-ID local-part and dot-atom domains).
        /// </summary>
        private static readonly uint[] AtomBitmap = CreateBitmap(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!#$%&'*+-/=?^_`{|}~");

        /// <summary>
        /// No-fold-literal bitmap (atom characters plus additional punctuation allowed inside <c>[...]</c> domain literals).
        /// </summary>
        private static readonly uint[] NormBitmap = CreateBitmap(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!#$%&'*+-/=?^_`{|}~\"(),.:;<@");

        /// <summary>
        /// Returns whether <paramref name="c"/> is a Message-ID atom character.
        /// </summary>
        /// <param name="c">Character to test.</param>
        /// <returns><see langword="true"/> when the character is an atom character.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsAtom(char c)
        {
            uint u = c;
            return u < 128 && (AtomBitmap[u >> 5] & (1u << (int)(u & 31))) != 0;
        }

        /// <summary>
        /// Returns whether <paramref name="c"/> is allowed in a Message-ID no-fold-literal domain segment.
        /// </summary>
        /// <param name="c">Character to test.</param>
        /// <returns><see langword="true"/> when the character is a norm character.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNorm(char c)
        {
            uint u = c;
            return u < 128 && (NormBitmap[u >> 5] & (1u << (int)(u & 31))) != 0;
        }

        /// <summary>
        /// Builds a 256-bit character set bitmap from the code points in <paramref name="chars"/>.
        /// </summary>
        /// <param name="chars">Characters to set in the bitmap.</param>
        /// <returns>Eight-word bitmap covering US-ASCII code points 0–127.</returns>
        private static uint[] CreateBitmap(ReadOnlySpan<char> chars)
        {
            uint[] bitmap = new uint[BitmapWordCount];
            foreach (char c in chars)
            {
                int code = c;
                bitmap[code >> 5] |= 1u << (code & 31);
            }

            return bitmap;
        }
    }
}
