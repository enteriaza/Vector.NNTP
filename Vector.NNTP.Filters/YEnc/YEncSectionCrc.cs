// <copyright file="YEncSectionCrc.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// YEncSectionCrc.cs -- yEnc section CRC-32 validation for NNTP article bodies.
//
// Thread safety:
//   All methods are static and stateless.

using System.IO.Hashing;

namespace Vector.NNTP.Filters.YEnc
{
    /// <summary>
    /// yEnc section CRC-32 validation for NNTP article bodies: scans <c>=ybegin</c>/<c>=yend</c> pairs, streams decode
    /// without materialising output, and compares CRC32 (and size when present).
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — line scanning uses <see cref="ArticleLineScanner"/>; decode streams into
    /// <see cref="Crc32"/> in fixed batches without allocating decoded buffers.</para>
    /// </remarks>
    public static class YEncSectionCrc
    {
        /// <summary>yEnc decode offset applied to non-escaped payload bytes (canonical yEnc value 42).</summary>
        private const int YEncOffset = 42;

        /// <summary>Additional subtract applied when decoding an escaped byte (second byte minus offset minus this delta).</summary>
        private const int YEncEscapedByteDelta = 64;

        /// <summary>Number of decoded bytes accumulated before appending to <see cref="Crc32"/> (stackalloc batch size).</summary>
        private const int CrcBatchSize = 512;

        /// <summary>Carriage return byte used only for NNTP line-boundary detection (not skipped inside encoded lines).</summary>
        private const byte CR = (byte)'\r';

        /// <summary>Line feed byte; may terminate a line alone or as part of CRLF.</summary>
        private const byte LF = (byte)'\n';

        /// <summary>yEnc escape prefix byte (<c>=</c>).</summary>
        private const byte EscapeChar = (byte)'=';

        /// <summary>Line prefix for a yEnc section begin line (<c>=ybegin </c>).</summary>
        private static ReadOnlySpan<byte> YEncBegin => "=ybegin "u8;

        /// <summary>Line prefix for a multipart part header (<c>=ypart </c>).</summary>
        private static ReadOnlySpan<byte> YEncPart => "=ypart "u8;

        /// <summary>Line prefix for a yEnc section end line (<c>=yend </c>).</summary>
        private static ReadOnlySpan<byte> YEncEnd => "=yend "u8;

        /// <summary>Multipart part CRC keyword without leading space (<c>pcrc32=</c>).</summary>
        private static ReadOnlySpan<byte> YEncPcrc32Key => "pcrc32="u8;

        /// <summary>Single-part CRC keyword without leading space (<c>crc32=</c>).</summary>
        private static ReadOnlySpan<byte> YEncCrc32Key => "crc32="u8;

        /// <summary>Multipart part CRC keyword as it appears in <c>=yend</c> metadata (<c> pcrc32=</c>).</summary>
        private static ReadOnlySpan<byte> YEncPcrc32KeyWithLeadingSpace => " pcrc32="u8;

        /// <summary>Single-part CRC keyword as it appears in <c>=yend</c> metadata (<c> crc32=</c>).</summary>
        private static ReadOnlySpan<byte> YEncCrc32KeyWithLeadingSpace => " crc32="u8;

        /// <summary>Declared decoded size keyword in <c>=yend</c> lines (<c> size=</c>).</summary>
        private static ReadOnlySpan<byte> YEncSizeKey => " size="u8;

        /// <summary>Per-line decoded cap keyword in <c>=ybegin</c> lines (<c> line=</c>); not enforced by <see cref="Validate"/>.</summary>
        private static ReadOnlySpan<byte> YEncLineKey => " line="u8;

        /// <summary>
        /// Validates all yEnc sections in an article body (bytes after the header/body blank line).
        /// </summary>
        /// <param name="body">Raw body bytes including CRLF line endings.</param>
        /// <returns>
        /// <see langword="true"/> if every section with a CRC keyword validates, or there are no yEnc sections;
        /// <see langword="false"/> on CRC mismatch or malformed structure (e.g. missing <c>=yend</c>).
        /// </returns>
        public static bool Validate(ReadOnlySpan<byte> body) => ValidateYEncSectionCrc(body);

        /// <summary>
        /// Scans the body for consecutive <c>=ybegin</c> sections, decodes each payload, and verifies CRC (and size when declared).
        /// </summary>
        /// <param name="body">Raw article body bytes.</param>
        /// <returns><see langword="false"/> on structural or checksum failure; <see langword="true"/> when all sections pass or none exist.</returns>
        private static bool ValidateYEncSectionCrc(ReadOnlySpan<byte> body)
        {
            int position = 0;

            while (position < body.Length)
            {
                int beginLineStart = ArticleLineScanner.FindLineStartingWith(body, position, YEncBegin);

                if (beginLineStart < 0)
                {
                    break;
                }

                int beginLineEnd = ArticleLineScanner.IndexOfCrLf(body, beginLineStart);

                if (beginLineEnd < 0)
                {
                    return false;
                }

                int afterBeginLine = ArticleLineScanner.AdvancePastLineTerminator(body, beginLineEnd);

                bool isMultiPart = false;
                int dataStart = afterBeginLine;

                if (afterBeginLine < body.Length && body[afterBeginLine..].StartsWith(YEncPart))
                {
                    isMultiPart = true;
                    int partLineEnd = ArticleLineScanner.IndexOfCrLf(body, afterBeginLine);

                    if (partLineEnd < 0)
                    {
                        return false;
                    }

                    dataStart = ArticleLineScanner.AdvancePastLineTerminator(body, partLineEnd);
                }

                if (!TryFindYEncEndLine(body, dataStart, isMultiPart, out int endLineStart, out int endLineEnd, out ReadOnlySpan<byte> endLine))
                {
                    return false;
                }

                ReadOnlySpan<byte> crcKey = isMultiPart ? YEncPcrc32KeyWithLeadingSpace : YEncCrc32KeyWithLeadingSpace;
                int keywordIndex = endLine.IndexOf(crcKey);
                if (keywordIndex < 0 && isMultiPart)
                {
                    // Some generators emit crc32= even on multipart sections. Prefer pcrc32= when present, but accept crc32= as fallback.
                    crcKey = YEncCrc32KeyWithLeadingSpace;
                    keywordIndex = endLine.IndexOf(crcKey);
                }

                // TryFindYEncEndLine guarantees the end line contains a CRC keyword.
                Debug.Assert(keywordIndex >= 0, "TryFindYEncEndLine guarantees a CRC keyword.");

                ReadOnlySpan<byte> afterKeyword = endLine[(keywordIndex + crcKey.Length)..];

                // If a CRC keyword is present but not parseable, treat the section as malformed and fail closed.
                if (!HexUInt32Parser.TryParseHexUInt32(afterKeyword, out uint declaredCrc))
                {
                    return false;
                }

                long declaredSize = -1;

                if (TryParseYEncDecimalValue(endLine, YEncSizeKey, out long parsedSize) && parsedSize >= 0)
                {
                    declaredSize = parsedSize;
                }

                ReadOnlySpan<byte> encodedPayload = body[dataStart..endLineStart];

                // yEnc "line=" is not a strict wire constraint and varies by encoder/escape ratio; do not enforce it here.
                if (!ComputeYEncDecodedCrc32(encodedPayload, declaredCrc, declaredSize, null, maxDecodedBytesPerLine: -1))
                {
                    return false;
                }

                position = endLineEnd >= 0 ? ArticleLineScanner.AdvancePastLineTerminator(body, endLineEnd) : body.Length;
            }

            return true;
        }

        /// <summary>
        /// Returns the first yEnc section's encoded payload slice and trailer fields (test and diagnostic helper).
        /// </summary>
        /// <param name="body">Raw article body bytes.</param>
        /// <param name="encodedPayload">Payload bytes between begin/part lines and the validated <c>=yend</c> line.</param>
        /// <param name="declaredCrc">CRC-32 value parsed from the end line.</param>
        /// <param name="declaredSize">Declared decoded size from <c> size=</c>, or -1 when omitted.</param>
        /// <param name="isMultiPart">When <see langword="true"/>, the section used multipart <c>=ypart</c> framing.</param>
        /// <returns><see langword="true"/> when a well-formed first section was located.</returns>
        internal static bool TryGetFirstSectionMetadata(
            ReadOnlySpan<byte> body,
            out ReadOnlySpan<byte> encodedPayload,
            out uint declaredCrc,
            out long declaredSize,
            out bool isMultiPart)
        {
            encodedPayload = default;
            declaredCrc = 0;
            declaredSize = -1;
            isMultiPart = false;

            int beginLineStart = ArticleLineScanner.FindLineStartingWith(body, 0, YEncBegin);

            if (beginLineStart < 0)
            {
                return false;
            }

            int beginLineEnd = ArticleLineScanner.IndexOfCrLf(body, beginLineStart);

            if (beginLineEnd < 0)
            {
                return false;
            }

            int afterBeginLine = ArticleLineScanner.AdvancePastLineTerminator(body, beginLineEnd);

            bool multi = false;
            int dataStart = afterBeginLine;

            if (afterBeginLine < body.Length && body[afterBeginLine..].StartsWith(YEncPart))
            {
                multi = true;
                int partLineEnd = ArticleLineScanner.IndexOfCrLf(body, afterBeginLine);

                if (partLineEnd < 0)
                {
                    return false;
                }

                dataStart = ArticleLineScanner.AdvancePastLineTerminator(body, partLineEnd);
            }

            if (!TryFindYEncEndLine(body, dataStart, multi, out int endLineStart, out int endLineEnd, out ReadOnlySpan<byte> endLine))
            {
                return false;
            }

            ReadOnlySpan<byte> crcKey = multi ? YEncPcrc32KeyWithLeadingSpace : YEncCrc32KeyWithLeadingSpace;
            int keywordIndex = endLine.IndexOf(crcKey);
            if (keywordIndex < 0 && multi)
            {
                crcKey = YEncCrc32KeyWithLeadingSpace;
                keywordIndex = endLine.IndexOf(crcKey);
            }

            if (keywordIndex < 0)
            {
                return false;
            }

            ReadOnlySpan<byte> afterKeyword = endLine[(keywordIndex + crcKey.Length)..];

            if (!HexUInt32Parser.TryParseHexUInt32(afterKeyword, out uint crcParsed))
            {
                return false;
            }

            long sizeParsed = -1;

            if (TryParseYEncDecimalValue(endLine, YEncSizeKey, out long parsedSize) && parsedSize >= 0)
            {
                sizeParsed = parsedSize;
            }

            encodedPayload = body[dataStart..endLineStart];
            declaredCrc = crcParsed;
            declaredSize = sizeParsed;
            isMultiPart = multi;
            return true;
        }

        /// <summary>
        /// Determines whether a line (without CRLF) is a yEnc control line that must be excluded from decoded CRC input.
        /// </summary>
        /// <param name="line">Line bytes without CRLF.</param>
        /// <returns><see langword="true"/> when the line is a yEnc control line.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsYEncControlLine(ReadOnlySpan<byte> line)
        {
            if (line.Length < 2 || line[0] != EscapeChar || line[1] != (byte)'y')
            {
                return false;
            }

            return line.StartsWith(YEncBegin)
                || line.StartsWith(YEncPart)
                || line.StartsWith(YEncEnd);
        }

        /// <summary>
        /// Parses a decimal integer after a keyword in a yEnc metadata line (e.g. <c> size=12345</c>).
        /// </summary>
        /// <param name="line">Metadata line bytes.</param>
        /// <param name="keyword">Keyword to search for (including leading space when applicable).</param>
        /// <param name="value">Parsed value on success.</param>
        /// <returns><see langword="true"/> when a value was parsed.</returns>
        public static bool TryParseYEncDecimalValue(ReadOnlySpan<byte> line, ReadOnlySpan<byte> keyword, out long value)
        {
            value = 0;
            int keywordIndex = line.IndexOf(keyword);

            if (keywordIndex < 0)
            {
                return false;
            }

            int digitStart = keywordIndex + keyword.Length;
            bool hasDigits = false;

            for (int i = digitStart; i < line.Length; i++)
            {
                int digit = line[i] - (byte)'0';

                if ((uint)digit > 9)
                {
                    break;
                }

                if (value > long.MaxValue / 10)
                {
                    return false;
                }

                value = (value * 10) + digit;
                hasDigits = true;
            }

            return hasDigits;
        }

        /// <summary>
        /// Computes CRC-32 and decoded length for a raw yEnc payload span (tests / diagnostics).
        /// </summary>
        /// <param name="encodedPayload">Payload bytes between yEnc begin/part lines and the yEnc end line.</param>
        /// <param name="materializeDecoded">Optional collector for decoded bytes (test-only).</param>
        /// <param name="crc32">CRC-32 result when the method returns <see langword="true"/>.</param>
        /// <param name="decodedByteCount">Decoded byte count when the method returns <see langword="true"/>.</param>
        /// <param name="maxDecodedBytesPerLine">Optional per-line decoded cap (<c>=ybegin line=N</c>); pass -1 to skip.</param>
        /// <returns><see langword="true"/> when decoding and CRC computation succeed.</returns>
        internal static bool TryComputeEncodedPayloadCrc32(
            ReadOnlySpan<byte> encodedPayload,
            List<byte>? materializeDecoded,
            out uint crc32,
            out long decodedByteCount,
            int maxDecodedBytesPerLine = -1)
        {
            Crc32 crc = new();
            Span<byte> batch = stackalloc byte[CrcBatchSize];
            int batchPos = 0;
            long decodedCount = 0;
            int lineStart = 0;

            while (lineStart < encodedPayload.Length)
            {
                int lineEnd = ArticleLineScanner.IndexOfCrLf(encodedPayload, lineStart);
                bool isLastLine = lineEnd < 0;
                int lineContentEnd = isLastLine ? encodedPayload.Length : lineEnd;

                ReadOnlySpan<byte> line = encodedPayload[lineStart..lineContentEnd];

                // NNTP body transparency: lines starting with ".." represent a single leading "." (RFC 3977).
                if (line.Length >= 2 && line[0] == (byte)'.' && line[1] == (byte)'.')
                {
                    line = line[1..];
                }

                if (line.Length > 0 && !IsYEncControlLine(line))
                {
                    if (batchPos == CrcBatchSize)
                    {
                        crc.Append(batch);
                        batchPos = 0;
                    }

                    long decodedThisLine = 0;

                    for (int i = 0; i < line.Length; i++)
                    {
                        byte b = line[i];

                        byte decoded;

                        if (b == EscapeChar)
                        {
                            if (i + 1 >= line.Length)
                            {
                                crc32 = 0;
                                decodedByteCount = 0;
                                return false;
                            }

                            byte sec = line[i + 1];
                            decoded = unchecked((byte)(sec - YEncOffset - YEncEscapedByteDelta));
                            i++;
                        }
                        else
                        {
                            decoded = (byte)(b - YEncOffset);
                        }

                        decodedThisLine++;

                        if (maxDecodedBytesPerLine >= 0 && decodedThisLine > maxDecodedBytesPerLine)
                        {
                            crc32 = 0;
                            decodedByteCount = 0;
                            return false;
                        }

                        batch[batchPos++] = decoded;
                        materializeDecoded?.Add(decoded);
                        decodedCount++;

                        if (batchPos == CrcBatchSize)
                        {
                            crc.Append(batch);
                            batchPos = 0;
                        }
                    }
                }

                lineStart = isLastLine ? encodedPayload.Length : ArticleLineScanner.AdvancePastLineTerminator(encodedPayload, lineEnd);
            }

            if (batchPos > 0)
            {
                crc.Append(batch[..batchPos]);
            }

            crc32 = crc.GetCurrentHashAsUInt32();
            decodedByteCount = decodedCount;
            return true;
        }

        /// <summary>
        /// Decodes <paramref name="encodedPayload"/>, compares CRC-32 to <paramref name="declaredCrc"/>, and optionally enforces <paramref name="declaredSize"/>.
        /// </summary>
        /// <param name="encodedPayload">Raw yEnc payload between control lines.</param>
        /// <param name="declaredCrc">CRC from the <c>=yend</c> line.</param>
        /// <param name="declaredSize">Expected decoded byte count, or -1 to skip size check.</param>
        /// <param name="materializeDecoded">Optional collector for test-only byte materialization.</param>
        /// <param name="maxDecodedBytesPerLine">Per-line decoded cap; pass -1 to skip (not used by <see cref="Validate"/>).</param>
        /// <returns><see langword="true"/> when decode succeeds and checksum (and size when set) match.</returns>
        private static bool ComputeYEncDecodedCrc32(
            ReadOnlySpan<byte> encodedPayload,
            uint declaredCrc,
            long declaredSize,
            List<byte>? materializeDecoded,
            int maxDecodedBytesPerLine = -1)
        {
            if (!TryComputeEncodedPayloadCrc32(encodedPayload, materializeDecoded, out uint crc32, out long decodedByteCount, maxDecodedBytesPerLine))
            {
                return false;
            }

            if (declaredSize >= 0 && decodedByteCount != declaredSize)
            {
                return false;
            }

            return crc32 == declaredCrc;
        }

        /// <summary>
        /// Locates the first plausible <c>=yend</c> line after <paramref name="startOffset"/> that includes both <c> size=</c> and a CRC keyword.
        /// </summary>
        /// <param name="body">Article body span.</param>
        /// <param name="startOffset">Index of the first payload byte to search from.</param>
        /// <param name="isMultiPart">When <see langword="true"/>, prefers <c> pcrc32=</c> but accepts <c> crc32=</c>.</param>
        /// <param name="endLineStart">Start index of the matched end line.</param>
        /// <param name="endLineEnd">Index of the line terminator before the end line content ends, or -1 for final line.</param>
        /// <param name="endLine">End line bytes without CRLF.</param>
        /// <returns><see langword="false"/> when no candidate with required metadata exists.</returns>
        private static bool TryFindYEncEndLine(
            ReadOnlySpan<byte> body,
            int startOffset,
            bool isMultiPart,
            out int endLineStart,
            out int endLineEnd,
            out ReadOnlySpan<byte> endLine)
        {
            endLineStart = -1;
            endLineEnd = -1;
            endLine = default;

            int search = startOffset;
            while (search < body.Length)
            {
                int candidateStart = ArticleLineScanner.FindLineStartingWith(body, search, YEncEnd);
                if (candidateStart < 0)
                {
                    return false;
                }

                int candidateEnd = ArticleLineScanner.IndexOfCrLf(body, candidateStart);
                int candidateLength = candidateEnd >= 0 ? candidateEnd - candidateStart : body.Length - candidateStart;
                ReadOnlySpan<byte> candidateLine = body.Slice(candidateStart, candidateLength);

                // Require a plausible yend metadata shape to avoid false positives from encoded data lines.
                bool hasSize = candidateLine.IndexOf(YEncSizeKey) >= 0;
                ReadOnlySpan<byte> key = isMultiPart ? YEncPcrc32KeyWithLeadingSpace : YEncCrc32KeyWithLeadingSpace;
                bool hasCrc = candidateLine.IndexOf(key) >= 0 || (isMultiPart && candidateLine.IndexOf(YEncCrc32KeyWithLeadingSpace) >= 0);

                if (hasSize && hasCrc)
                {
                    endLineStart = candidateStart;
                    endLineEnd = candidateEnd;
                    endLine = candidateLine;
                    return true;
                }

                if (candidateEnd < 0)
                {
                    return false;
                }

                search = ArticleLineScanner.AdvancePastLineTerminator(body, candidateEnd);
            }

            return false;
        }
    }
}

