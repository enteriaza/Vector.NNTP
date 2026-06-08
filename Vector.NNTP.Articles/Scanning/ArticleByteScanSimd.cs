// <copyright file="ArticleByteScanSimd.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: Vector256/Vector128 SIMD byte scanning shared across transit spool preprocessing, classification, and spamd synthesis.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Vector.NNTP.Articles.Scanning
{
    /// <summary>
    /// SIMD-accelerated byte scanning for raw NNTP article buffers (newline search, header/body separator location, and
    /// ASCII case-insensitive prefix/substring tests).
    /// </summary>
    /// <remarks>
    /// <para><b>Role:</b> Centralizes hot-path header and body scans so spool preprocessing, classification, logging, and
    /// spamd synthesis share one implementation. Consumers include <see cref="Classification.ArticleTypeClassifier"/>,
    /// <see cref="Processing.ArticleSpoolPreprocessor"/>, <see cref="Processing.ArticleSpoolPostprocessor"/>,
    /// <see cref="Processing.ArticlePathHeaderMutator"/>, <see cref="Processing.SpamdScanArticleBuilder"/>,
    /// <see cref="Logging.CancelControlHeaderParser"/>, and <see cref="Logging.PathHeaderFeedResolver"/>.</para>
    /// <para><b>Performance:</b> HOT PATH — prefers <see cref="Vector256"/> when
    /// <see cref="Vector256.IsHardwareAccelerated"/>, then <see cref="Vector128"/> when
    /// <see cref="Vector128.IsHardwareAccelerated"/>, then scalar tails. Methods avoid heap allocation except
    /// <see cref="ContainsAsciiIgnoreCase"/>, which uses subspan slicing per candidate offset.</para>
    /// <para>
    /// <b>Header/body separator:</b> Recognizes the first <c>\n\n</c> or <c>\r\n\r\n</c> in the buffer (Diablo/INN-style
    /// terminator semantics). A lone <c>\r\n</c> between header lines does not end the header phase; the blank line requires
    /// two consecutive line breaks.
    /// </para>
    /// <para>
    /// <b>ASCII case folding:</b> <see cref="ToLowerAscii"/> and the SIMD fold helpers add 32 to bytes in the
    /// <c>A</c>–<c>Z</c> range so <see cref="StartsWithAsciiIgnoreCase"/> and <see cref="ContainsAsciiIgnoreCase"/> match
    /// header tokens case-insensitively. Bytes outside ASCII uppercase are not normalized (for example UTF-8 multibyte
    /// sequences pass through unchanged).
    /// </para>
    /// <para>
    /// <b>Contracts:</b> All members are static, stateless, and do not throw for well-formed caller inputs. Callers must
    /// keep ranged scan bounds within <see cref="ReadOnlySpan{T}.Length"/> when calling
    /// <see cref="IndexOfLineFeed"/>; out-of-range indices are not validated defensively on the hot path.
    /// </para>
    /// <para><b>Platform:</b> Tuned for x64 builds where Vector256/Vector128 hardware acceleration is expected.</para>
    /// <para><b>Threading:</b> Safe for concurrent writer pumps without external synchronization.</para>
    /// </remarks>
    internal static class ArticleByteScanSimd
    {
        /// <summary>
        /// Line feed byte used as the primary line delimiter in article scans.
        /// </summary>
        /// <remarks>
        /// Value is ASCII <c>0x0A</c> (<c>\n</c>). <see cref="IndexOfLineFeed"/> and separator detection scan for this byte;
        /// <see cref="CarriageReturn"/> alone is not treated as a line terminator.
        /// </remarks>
        private const byte LineFeed = (byte)'\n';

        /// <summary>
        /// Carriage return byte paired with <see cref="LineFeed"/> in <c>\r\n</c> line endings.
        /// </summary>
        /// <remarks>
        /// Value is ASCII <c>0x0D</c> (<c>\r</c>). Used only when validating <c>\r\n\r\n</c> header/body separators in
        /// <see cref="TryMatchHeaderSeparator"/>.
        /// </remarks>
        private const byte CarriageReturn = (byte)'\r';

        /// <summary>
        /// Number of bytes processed per 128-bit SIMD lane group.
        /// </summary>
        /// <remarks>
        /// Scalar tail handles the final 0–15 bytes when the span length is not a multiple of 16.
        /// </remarks>
        private const int Vector128ByteCount = 16;

        /// <summary>
        /// Number of bytes processed per 256-bit SIMD lane group.
        /// </summary>
        /// <remarks>
        /// Scalar or Vector128 tail handles the final 0–31 bytes when the span length is not a multiple of 32.
        /// </remarks>
        private const int Vector256ByteCount = 32;

        /// <summary>
        /// Broadcast ASCII <c>A</c> for uppercase-range SIMD folding in 128-bit lanes.
        /// </summary>
        /// <remarks>
        /// Initialized once; paired with <see cref="UpperAsciiHiVec128"/> and <see cref="AsciiCaseFoldDeltaVec128"/> by
        /// <see cref="FoldAsciiUpperToLowerVector128"/>.
        /// </remarks>
        private static readonly Vector128<byte> UpperAsciiLoVec128 = Vector128.Create((byte)'A');

        /// <summary>
        /// Broadcast ASCII <c>Z</c> for uppercase-range SIMD folding in 128-bit lanes.
        /// </summary>
        /// <remarks>
        /// Initialized once; paired with <see cref="UpperAsciiLoVec128"/> and <see cref="AsciiCaseFoldDeltaVec128"/> by
        /// <see cref="FoldAsciiUpperToLowerVector128"/>.
        /// </remarks>
        private static readonly Vector128<byte> UpperAsciiHiVec128 = Vector128.Create((byte)'Z');

        /// <summary>
        /// Broadcast ASCII case-fold delta (32) for uppercase SIMD folding in 128-bit lanes.
        /// </summary>
        /// <remarks>
        /// Added to uppercase lanes by <see cref="FoldAsciiUpperToLowerVector128"/> to mirror <see cref="ToLowerAscii"/>.
        /// </remarks>
        private static readonly Vector128<byte> AsciiCaseFoldDeltaVec128 = Vector128.Create((byte)32);

        /// <summary>
        /// Broadcast ASCII <c>A</c> for uppercase-range SIMD folding in 256-bit lanes.
        /// </summary>
        /// <remarks>
        /// Initialized once; paired with <see cref="UpperAsciiHiVec256"/> and <see cref="AsciiCaseFoldDeltaVec256"/> by
        /// <see cref="FoldAsciiUpperToLowerVector256"/>.
        /// </remarks>
        private static readonly Vector256<byte> UpperAsciiLoVec256 = Vector256.Create((byte)'A');

        /// <summary>
        /// Broadcast ASCII <c>Z</c> for uppercase-range SIMD folding in 256-bit lanes.
        /// </summary>
        /// <remarks>
        /// Initialized once; paired with <see cref="UpperAsciiLoVec256"/> and <see cref="AsciiCaseFoldDeltaVec256"/> by
        /// <see cref="FoldAsciiUpperToLowerVector256"/>.
        /// </remarks>
        private static readonly Vector256<byte> UpperAsciiHiVec256 = Vector256.Create((byte)'Z');

        /// <summary>
        /// Broadcast ASCII case-fold delta (32) for uppercase SIMD folding in 256-bit lanes.
        /// </summary>
        /// <remarks>
        /// Added to uppercase lanes by <see cref="FoldAsciiUpperToLowerVector256"/> to mirror <see cref="ToLowerAscii"/>.
        /// </remarks>
        private static readonly Vector256<byte> AsciiCaseFoldDeltaVec256 = Vector256.Create((byte)32);

        /// <summary>
        /// Finds the first line feed at or after <paramref name="start"/> and before <paramref name="endExclusive"/>.
        /// </summary>
        /// <param name="span">Article buffer being scanned.</param>
        /// <param name="start">Inclusive scan start. When <paramref name="start"/> is not less than
        /// <paramref name="endExclusive"/>, the method returns <paramref name="endExclusive"/> without reading
        /// <paramref name="span"/>.</param>
        /// <param name="endExclusive">
        /// Exclusive scan end. Callers typically pass <see cref="FindHeaderEnd"/> when iterating header lines, or
        /// <see cref="ReadOnlySpan{T}.Length"/> for body-bounded scans.
        /// </param>
        /// <returns>
        /// Index of the first <see cref="LineFeed"/> in the half-open range from <paramref name="start"/> through
        /// <paramref name="endExclusive"/> minus one, or <paramref name="endExclusive"/> when none exists.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Used by header validation, header parsing, classification line iteration, path-header scans, and spamd header
        /// preservation. Does not treat <see cref="CarriageReturn"/> alone as a line terminator; callers strip trailing
        /// <c>\r</c> from line content when needed.
        /// </para>
        /// <para>
        /// When hardware acceleration is available, scans in <see cref="Vector256ByteCount"/>-byte then
        /// <see cref="Vector128ByteCount"/>-byte chunks via <see cref="Vector256.ExtractMostSignificantBits"/> /
        /// <see cref="Vector128.ExtractMostSignificantBits"/> before falling back to a per-byte scalar loop.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int IndexOfLineFeed(ReadOnlySpan<byte> span, int start, int endExclusive)
        {
            if ((uint)start >= (uint)endExclusive)
            {
                return endExclusive;
            }

            ref byte searchRef = ref MemoryMarshal.GetReference(span);
            int i = start;

            if (Vector256.IsHardwareAccelerated)
            {
                Vector256<byte> lfVec = Vector256.Create(LineFeed);
                int simdEnd = endExclusive - Vector256ByteCount;
                while (i <= simdEnd)
                {
                    Vector256<byte> chunk = Vector256.LoadUnsafe(ref searchRef, (nuint)i);
                    ulong mask = Vector256.ExtractMostSignificantBits(Vector256.Equals(chunk, lfVec));
                    if (mask != 0)
                    {
                        return i + BitOperations.TrailingZeroCount(mask);
                    }

                    i += Vector256ByteCount;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                Vector128<byte> lfVec = Vector128.Create(LineFeed);
                int simdEnd = endExclusive - Vector128ByteCount;
                while (i <= simdEnd)
                {
                    Vector128<byte> chunk = Vector128.LoadUnsafe(ref searchRef, (nuint)i);
                    uint mask = Vector128.ExtractMostSignificantBits(Vector128.Equals(chunk, lfVec));
                    if (mask != 0)
                    {
                        return i + BitOperations.TrailingZeroCount(mask);
                    }

                    i += Vector128ByteCount;
                }
            }

            while (i < endExclusive)
            {
                if (Unsafe.Add(ref searchRef, (nint)(uint)i) == LineFeed)
                {
                    return i;
                }

                i++;
            }

            return endExclusive;
        }

        /// <summary>
        /// Returns the exclusive upper bound for header-phase line iteration.
        /// </summary>
        /// <param name="span">Full article bytes.</param>
        /// <returns>
        /// Header-phase boundary index from <see cref="FindHeaderSeparator"/>: for <c>\n\n</c>, the index of the second
        /// <see cref="LineFeed"/>; for <c>\r\n\r\n</c>, the index of the second <see cref="CarriageReturn"/> in the
        /// separator; or <c>-1</c> when no recognized separator exists. Callers iterate header lines with
        /// <c>index &lt; headerEnd</c>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// For <c>Header: value\r\n\r\nBody</c>, returns the index of the second <c>\r</c> in the separator — the start
        /// of the blank line. Header field lines lie strictly before this offset.
        /// </para>
        /// <para>
        /// Delegates to <see cref="FindHeaderSeparator"/> and returns only the header boundary component. Paired with
        /// <see cref="FindBodyStart"/> for body scan budgeting in <see cref="Classification.ArticleTypeClassifier"/> and
        /// header-only validation in <see cref="Processing.ArticleSpoolPreprocessor"/>.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int FindHeaderEnd(ReadOnlySpan<byte> span)
        {
            (int headerEnd, _) = FindHeaderSeparator(span);
            return headerEnd;
        }

        /// <summary>
        /// Locates the first body byte immediately after the header/body separator.
        /// </summary>
        /// <param name="span">Full article bytes.</param>
        /// <returns>
        /// Index of the first body octet after <c>\r\n\r\n</c> or <c>\n\n</c>, or <c>-1</c> when no separator is found.
        /// </returns>
        /// <remarks>
        /// <para>
        /// For <c>\r\n\r\n</c>, returns the index after the second <see cref="LineFeed"/> (first byte of body content).
        /// For <c>\n\n</c>, returns the index after the second <see cref="LineFeed"/>. Delegates to
        /// <see cref="FindHeaderSeparator"/>.
        /// </para>
        /// <para>
        /// <see cref="Processing.SpamdScanArticleBuilder"/> advances past contiguous <c>\r</c>/<c>\n</c> bytes when this
        /// method returns <c>-1</c> as a defensive fallback.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int FindBodyStart(ReadOnlySpan<byte> span)
        {
            (_, int bodyStart) = FindHeaderSeparator(span);
            return bodyStart;
        }

        /// <summary>
        /// Locates the header/body separator and returns indices for header-phase classification and body scan budgeting.
        /// </summary>
        /// <param name="span">Full article bytes (headers are not length-capped by this method).</param>
        /// <returns>
        /// <c>(headerEnd, bodyStart)</c> where <c>headerEnd</c> is the header-phase boundary index and <c>bodyStart</c>
        /// is the first body byte after the separator; both are <c>-1</c> when no recognized separator exists.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Returns <c>(-1, -1)</c> when <paramref name="span"/>.Length is less than 2. Uses SIMD to find candidate
        /// <see cref="LineFeed"/> bytes, then validates <c>\r\n\r\n</c> and <c>\n\n</c> patterns in ascending offset order
        /// so the earliest separator in the buffer wins.
        /// </para>
        /// <para><b><c>\n\n</c>:</b> when the first <see cref="LineFeed"/> of the pair is at index <c>i</c> and
        /// <c>span[i + 1]</c> is also <see cref="LineFeed"/>, <c>headerEnd = i + 1</c>, <c>bodyStart = i + 2</c>.</para>
        /// <para><b><c>\r\n\r\n</c>:</b> when <see cref="LineFeed"/> at index <c>i</c> is preceded by
        /// <see cref="CarriageReturn"/> and followed by <see cref="CarriageReturn"/> then <see cref="LineFeed"/>,
        /// <c>headerEnd = i + 1</c>, <c>bodyStart = i + 3</c> (byte after the second <see cref="LineFeed"/>).</para>
        /// <example>
        /// For bytes <c>Subject: test\n\nbody</c> (LF-only): if the first <c>\n\n</c> starts at index 13,
        /// <c>headerEnd = 14</c>, <c>bodyStart = 15</c>.
        /// </example>
        /// </remarks>
        internal static (int HeaderEnd, int BodyStart) FindHeaderSeparator(ReadOnlySpan<byte> span)
        {
            int length = span.Length;
            if (length < 2)
            {
                return (-1, -1);
            }

            ref byte searchRef = ref MemoryMarshal.GetReference(span);
            int i = 0;
            int scanEnd = length - 1;

            if (Vector256.IsHardwareAccelerated)
            {
                Vector256<byte> lfVec = Vector256.Create(LineFeed);
                int simdEnd = scanEnd - Vector256ByteCount;
                while (i <= simdEnd)
                {
                    Vector256<byte> chunk = Vector256.LoadUnsafe(ref searchRef, (nuint)i);
                    ulong mask = Vector256.ExtractMostSignificantBits(Vector256.Equals(chunk, lfVec));
                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int pos = i + bit;
                        if (TryMatchHeaderSeparator(span, pos, out int headerEnd, out int bodyStart))
                        {
                            return (headerEnd, bodyStart);
                        }

                        mask &= mask - 1;
                    }

                    i += Vector256ByteCount;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                Vector128<byte> lfVec = Vector128.Create(LineFeed);
                int simdEnd = scanEnd - Vector128ByteCount;
                while (i <= simdEnd)
                {
                    Vector128<byte> chunk = Vector128.LoadUnsafe(ref searchRef, (nuint)i);
                    uint mask = Vector128.ExtractMostSignificantBits(Vector128.Equals(chunk, lfVec));
                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int pos = i + bit;
                        if (TryMatchHeaderSeparator(span, pos, out int headerEnd, out int bodyStart))
                        {
                            return (headerEnd, bodyStart);
                        }

                        mask &= mask - 1;
                    }

                    i += Vector128ByteCount;
                }
            }

            for (; i < scanEnd; i++)
            {
                if (Unsafe.Add(ref searchRef, (nint)(uint)i) == LineFeed &&
                    TryMatchHeaderSeparator(span, i, out int headerEnd, out int bodyStart))
                {
                    return (headerEnd, bodyStart);
                }
            }

            return (-1, -1);
        }

        /// <summary>
        /// Tests whether a <see cref="LineFeed"/> at <paramref name="lineFeedIndex"/> completes a recognized header/body
        /// separator.
        /// </summary>
        /// <param name="span">Full article bytes.</param>
        /// <param name="lineFeedIndex">Candidate <see cref="LineFeed"/> offset.</param>
        /// <param name="headerEnd">
        /// When this method returns <see langword="true"/>, the header-phase boundary index; set to <c>-1</c> when this
        /// method returns <see langword="false"/>.
        /// </param>
        /// <param name="bodyStart">
        /// When this method returns <see langword="true"/>, the first body byte index; set to <c>-1</c> when this method
        /// returns <see langword="false"/>.
        /// </param>
        /// <returns><see langword="true"/> when <paramref name="lineFeedIndex"/> participates in a valid separator.</returns>
        /// <remarks>
        /// <para>
        /// Invoked from SIMD and scalar separator scans with each candidate <see cref="LineFeed"/> offset. For
        /// <c>\r\n\r\n</c>, <paramref name="lineFeedIndex"/> must reference the first <see cref="LineFeed"/> of the
        /// four-byte pattern (the byte immediately after the first <see cref="CarriageReturn"/>).
        /// </para>
        /// <para>
        /// Does not match a lone <c>\r\n</c> at end-of-headers; a second line break is required. Returns
        /// <see langword="false"/> when bounds checks for the candidate pattern fail.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryMatchHeaderSeparator(
            ReadOnlySpan<byte> span,
            int lineFeedIndex,
            out int headerEnd,
            out int bodyStart)
        {
            headerEnd = -1;
            bodyStart = -1;

            if (lineFeedIndex + 1 < span.Length && span[lineFeedIndex + 1] == LineFeed)
            {
                headerEnd = lineFeedIndex + 1;
                bodyStart = lineFeedIndex + 2;
                return true;
            }

            if (lineFeedIndex >= 1 &&
                span[lineFeedIndex - 1] == CarriageReturn &&
                lineFeedIndex + 2 < span.Length &&
                span[lineFeedIndex + 1] == CarriageReturn &&
                span[lineFeedIndex + 2] == LineFeed)
            {
                headerEnd = lineFeedIndex + 1;
                bodyStart = lineFeedIndex + 3;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tests whether <paramref name="line"/> begins with <paramref name="prefix"/> using ASCII case folding.
        /// </summary>
        /// <param name="line">Candidate header or body line bytes (typically a slice ending before <c>\r</c> or <c>\n</c>).</param>
        /// <param name="prefix">Expected ASCII prefix (for example <c>Content-Type: </c> or <c>path</c>).</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="line"/> is at least as long as <paramref name="prefix"/> and each
        /// byte matches under <see cref="ToLowerAscii"/>; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Returns <see langword="false"/> immediately when <paramref name="line"/>.Length is less than
        /// <paramref name="prefix"/>.Length. An empty <paramref name="prefix"/> therefore matches any line.
        /// </para>
        /// <para><b>SIMD tiers:</b></para>
        /// <list type="bullet">
        /// <item><description>Prefix length 32 or more: first 32 bytes compared with Vector256 when accelerated.</description></item>
        /// <item><description>Prefix length 16–31: first 16 bytes compared with Vector128 when accelerated and Vector256 did not run.</description></item>
        /// <item><description>Remaining bytes (or entire prefix when shorter than 16): scalar <see cref="ToLowerAscii"/> comparison.</description></item>
        /// </list>
        /// <para>Does not allocate when the SIMD fast paths succeed; scalar tail compares byte-by-byte in place.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool StartsWithAsciiIgnoreCase(ReadOnlySpan<byte> line, ReadOnlySpan<byte> prefix)
        {
            int prefixLength = prefix.Length;
            if (line.Length < prefixLength)
            {
                return false;
            }

            ref byte lineRef = ref MemoryMarshal.GetReference(line);
            ref byte prefixRef = ref MemoryMarshal.GetReference(prefix);
            int i = 0;

            if (Vector256.IsHardwareAccelerated && prefixLength >= Vector256ByteCount)
            {
                Vector256<byte> lineChunk = Vector256.LoadUnsafe(ref lineRef);
                Vector256<byte> prefixChunk = Vector256.LoadUnsafe(ref prefixRef);
                if (!AsciiEqualsIgnoreCaseVector256(lineChunk, prefixChunk))
                {
                    return false;
                }

                i = Vector256ByteCount;
            }

            if (Vector128.IsHardwareAccelerated && prefixLength >= Vector128ByteCount && i == 0)
            {
                Vector128<byte> lineChunk = Vector128.LoadUnsafe(ref lineRef);
                Vector128<byte> prefixChunk = Vector128.LoadUnsafe(ref prefixRef);
                if (!AsciiEqualsIgnoreCaseVector128(lineChunk, prefixChunk))
                {
                    return false;
                }

                i = Vector128ByteCount;
            }

            for (; i < prefixLength; i++)
            {
                if (ToLowerAscii(Unsafe.Add(ref lineRef, (nint)(uint)i)) !=
                    ToLowerAscii(Unsafe.Add(ref prefixRef, (nint)(uint)i)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether <paramref name="line"/> contains <paramref name="needle"/> under ASCII case-insensitive comparison.
        /// </summary>
        /// <param name="line">Haystack bytes to search (typically a single header line).</param>
        /// <param name="needle">ASCII substring to locate (for example a MIME parameter token).</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="needle"/> is empty or a case-insensitive match exists within
        /// <paramref name="line"/>; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Scalar O(n×m) search used where prefix-only checks are insufficient — for example MIME parameter tokens and NZB
        /// poster hint matching in <see cref="Classification.ArticleTypeClassifier"/>. Each candidate offset calls
        /// <see cref="StartsWithAsciiIgnoreCase"/> on a subspan, which may allocate a slice object on the stack/heap
        /// depending on runtime optimizations.
        /// </para>
        /// <para>Prefer <see cref="StartsWithAsciiIgnoreCase"/> when the match position is known to be at line start.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ContainsAsciiIgnoreCase(ReadOnlySpan<byte> line, ReadOnlySpan<byte> needle)
        {
            if (needle.IsEmpty)
            {
                return true;
            }

            if (line.Length < needle.Length)
            {
                return false;
            }

            int lastStart = line.Length - needle.Length;
            for (int start = 0; start <= lastStart; start++)
            {
                if (StartsWithAsciiIgnoreCase(line[start..], needle))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Lowercases an ASCII byte for case-insensitive NNTP header and body prefix matching.
        /// </summary>
        /// <param name="value">Input byte in the range 0–255.</param>
        /// <returns>
        /// <paramref name="value"/> plus 32 when it is ASCII <c>A</c>–<c>Z</c>; otherwise <paramref name="value"/>
        /// unchanged. Non-ASCII bytes are passed through without transformation.
        /// </returns>
        /// <remarks>
        /// Scalar fallback used by <see cref="StartsWithAsciiIgnoreCase"/> tails and by callers that inspect individual header
        /// bytes (for example first-character shortcuts in <see cref="Classification.ArticleTypeClassifier"/>).
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte ToLowerAscii(byte value)
        {
            return value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;
        }

        /// <summary>
        /// Compares two 32-byte vectors for ASCII case-insensitive equality.
        /// </summary>
        /// <param name="left">Left-hand line bytes loaded from the haystack.</param>
        /// <param name="right">Right-hand prefix bytes loaded from the expected prefix.</param>
        /// <returns><see langword="true"/> when all 32 lanes match under ASCII case folding.</returns>
        /// <remarks>
        /// Folds both operands with <see cref="FoldAsciiUpperToLowerVector256"/> before
        /// <see cref="Vector256.EqualsAll"/>. Used only from <see cref="StartsWithAsciiIgnoreCase"/> when the prefix
        /// length is at least 32.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AsciiEqualsIgnoreCaseVector256(Vector256<byte> left, Vector256<byte> right)
        {
            return Vector256.EqualsAll(
                FoldAsciiUpperToLowerVector256(left),
                FoldAsciiUpperToLowerVector256(right));
        }

        /// <summary>
        /// Compares two 16-byte vectors for ASCII case-insensitive equality.
        /// </summary>
        /// <param name="left">Left-hand line bytes loaded from the haystack.</param>
        /// <param name="right">Right-hand prefix bytes loaded from the expected prefix.</param>
        /// <returns><see langword="true"/> when all 16 lanes match under ASCII case folding.</returns>
        /// <remarks>
        /// Folds both operands with <see cref="FoldAsciiUpperToLowerVector128"/> before
        /// <see cref="Vector128.EqualsAll"/>. Used only from <see cref="StartsWithAsciiIgnoreCase"/> when the Vector256 fast
        /// path did not run and the prefix length is at least 16.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AsciiEqualsIgnoreCaseVector128(Vector128<byte> left, Vector128<byte> right)
        {
            return Vector128.EqualsAll(
                FoldAsciiUpperToLowerVector128(left),
                FoldAsciiUpperToLowerVector128(right));
        }

        /// <summary>
        /// Folds ASCII <c>A</c>–<c>Z</c> to lowercase in a 32-byte vector by adding 32 to uppercase lanes; other bytes pass through unchanged.
        /// </summary>
        /// <param name="value">Input bytes.</param>
        /// <returns>Case-folded bytes with the same lane-wise semantics as <see cref="ToLowerAscii"/> applied per byte.</returns>
        /// <remarks>
        /// Uses cached <see cref="UpperAsciiLoVec256"/>, <see cref="UpperAsciiHiVec256"/>, and
        /// <see cref="AsciiCaseFoldDeltaVec256"/> so hot-path calls avoid per-invocation vector construction.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<byte> FoldAsciiUpperToLowerVector256(Vector256<byte> value)
        {
            Vector256<byte> isUpper = Vector256.BitwiseAnd(
                Vector256.GreaterThanOrEqual(value, UpperAsciiLoVec256),
                Vector256.LessThanOrEqual(value, UpperAsciiHiVec256));
            return Vector256.Add(value, Vector256.BitwiseAnd(isUpper, AsciiCaseFoldDeltaVec256));
        }

        /// <summary>
        /// Folds ASCII <c>A</c>–<c>Z</c> to lowercase in a 16-byte vector by adding 32 to uppercase lanes; other bytes pass through unchanged.
        /// </summary>
        /// <param name="value">Input bytes.</param>
        /// <returns>Case-folded bytes with the same lane-wise semantics as <see cref="ToLowerAscii"/> applied per byte.</returns>
        /// <remarks>
        /// Uses cached <see cref="UpperAsciiLoVec128"/>, <see cref="UpperAsciiHiVec128"/>, and
        /// <see cref="AsciiCaseFoldDeltaVec128"/> so hot-path calls avoid per-invocation vector construction.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> FoldAsciiUpperToLowerVector128(Vector128<byte> value)
        {
            Vector128<byte> isUpper = Vector128.BitwiseAnd(
                Vector128.GreaterThanOrEqual(value, UpperAsciiLoVec128),
                Vector128.LessThanOrEqual(value, UpperAsciiHiVec128));
            return Vector128.Add(value, Vector128.BitwiseAnd(isUpper, AsciiCaseFoldDeltaVec128));
        }
    }
}
