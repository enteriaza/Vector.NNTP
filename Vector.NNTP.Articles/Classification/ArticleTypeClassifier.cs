// <copyright file="ArticleTypeClassifier.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: Diablo arttype.c line scanner for yEnc/binary routing on every transit spool article.

using System.Text;
using Vector.NNTP.Articles.Scanning;

namespace Vector.NNTP.Articles.Classification
{
    /// <summary>
    /// Classifies NNTP article content using a Diablo <c>arttype.c</c>-aligned header and body line scanner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Invoked by <see cref="Processing.ArticleSpoolPostprocessor"/> after header semantics pass and
    /// before yEnc CRC or SpamAssassin filtering. The returned <see cref="ArticleTypeFlags"/> drive spool-path decisions
    /// (yEnc validation vs spam scan gate), <see cref="Metrics.NntpSpoolMetrics.RecordArticleTypes"/>, and future policy.
    /// </para>
    /// <para><b>Scan invariant:</b></para>
    /// <list type="bullet">
    /// <item><description><b>Header classification always scans the entire header block.</b></description></item>
    /// <item>
    /// <description>
    /// <b>Body classification may terminate early when a concrete encoding (<see cref="ArticleTypeFlags.YEnc"/>,
    /// <see cref="ArticleTypeFlags.UuEncode"/>, <see cref="ArticleTypeFlags.Base64"/>, or
    /// <see cref="ArticleTypeFlags.BinHex"/>) is identified</b> (or when <see cref="MaxClassificationBytes"/> is
    /// reached).
    /// </description>
    /// </item>
    /// </list>
    /// <para><b>Scan scope:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// Classify every header line through the <c>\r\n\r\n</c> or <c>\n\n</c> separator discovered by
    /// <see cref="ArticleByteScanSimd.FindHeaderSeparator"/> (no header-size cap).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Classify body lines after the separator until <see cref="MaxClassificationBytes"/> of body octets or early exit.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>yEnc detection:</b> <c>=ybegin</c> lines are evaluated on every physical line (header or body) before the
    /// header/body branch in <see cref="Scanner.ProcessLine"/>.
    /// </para>
    /// <para><b>Performance:</b> Line and separator scans delegate to <see cref="ArticleByteScanSimd"/>.</para>
    /// <para><b>Thread safety:</b> Stateless static methods; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal static class ArticleTypeClassifier
    {
        /// <summary>
        /// Maximum number of body bytes scanned for classification after the header/body separator.
        /// </summary>
        /// <value><c>65_536</c> (64 KiB).</value>
        /// <remarks>
        /// <para>
        /// <see cref="Classify"/> computes <c>scanLimit = min(articleLength, bodyStart + MaxClassificationBytes)</c> when a
        /// separator is found. Header octets before the body start offset are always included regardless of this cap.
        /// </para>
        /// <para>Articles with no header/body separator are scanned in full as header-only content (no body cap applied).</para>
        /// </remarks>
        public const int MaxClassificationBytes = 65_536;

        /// <summary>
        /// Minimum <c>Newsgroups:</c> comma-separated token count that sets <see cref="ArticleTypeFlags.MassCrosspost"/> at
        /// finalize.
        /// </summary>
        /// <value><c>10</c>.</value>
        /// <remarks>
        /// Compared with <c>&gt;=</c> against <see cref="HeaderValueUtilities.CountCommaSeparatedTokens"/> output in
        /// <see cref="Scanner.CompleteClassification"/>.
        /// </remarks>
        internal const int MassCrosspostThreshold = 10;

        /// <summary>
        /// Bitmask of body encoding flags that stop further body line classification once any bit is set.
        /// </summary>
        /// <value>
        /// <see cref="ArticleTypeFlags.YEnc"/> | <see cref="ArticleTypeFlags.UuEncode"/> |
        /// <see cref="ArticleTypeFlags.Base64"/> | <see cref="ArticleTypeFlags.BinHex"/>.
        /// </value>
        /// <remarks>
        /// Generic <see cref="ArticleTypeFlags.Binary"/> from MIME headers alone does not qualify. Checked in
        /// <see cref="Classify"/> and <see cref="Scanner.ProcessLine"/> before <see cref="Scanner.ProcessBodyLine"/>.
        /// </remarks>
        private const ArticleTypeFlags BodyEncodingIdentified =
            ArticleTypeFlags.YEnc |
            ArticleTypeFlags.UuEncode |
            ArticleTypeFlags.Base64 |
            ArticleTypeFlags.BinHex;

        /// <summary>
        /// Known automated NZB poster tokens matched case-insensitively in <c>X-Newsposter:</c> or <c>User-Agent:</c> headers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Extend this list as new posting tools appear; matching uses
        /// <see cref="ArticleByteScanSimd.ContainsAsciiIgnoreCase"/> against precomputed
        /// <see cref="NzbPosterHintBytes"/>.
        /// </para>
        /// <para>Order is not significant; scanning stops at the first hint match on a line.</para>
        /// </remarks>
        internal static readonly string[] NzbPosterHints =
        [
            "Nyuu",
            "ngPost",
            "JBinUp",
            "PowerPost",
            "NewsUP",
            "YEnc-PowerPost",
        ];

        /// <summary>
        /// ASCII byte forms of <see cref="NzbPosterHints"/> for allocation-free substring matching on header lines.
        /// </summary>
        /// <remarks>
        /// Initialized once by <see cref="CreateNzbPosterHintBytes"/> at type load. Each inner array is a literal hint
        /// encoded with <see cref="Encoding.ASCII"/>.
        /// </remarks>
        private static readonly byte[][] NzbPosterHintBytes = CreateNzbPosterHintBytes();

        /// <summary>
        /// Line-type bit for UU-encoded payload lines in <see cref="ClassifyLineAsTypes"/>.
        /// </summary>
        /// <value><c>0x01</c>.</value>
        private const byte Uue = 0x01;

        /// <summary>
        /// Line-type bit for Base64 payload lines in <see cref="ClassifyLineAsTypes"/>.
        /// </summary>
        /// <value><c>0x02</c>.</value>
        private const byte B64 = 0x02;

        /// <summary>
        /// Line-type bit for BinHex payload lines in <see cref="ClassifyLineAsTypes"/>.
        /// </summary>
        /// <value><c>0x04</c>.</value>
        private const byte Bhx = 0x04;

        /// <summary>
        /// Union of all line-type bits tested by <see cref="ClassifyLineAsTypes"/>.
        /// </summary>
        /// <value><see cref="Uue"/> | <see cref="B64"/> | <see cref="Bhx"/>.</value>
        private const byte AllLineTypes = Uue | B64 | Bhx;

        /// <summary>
        /// Exposes the Diablo-derived per-byte character class lookup table as a span.
        /// </summary>
        /// <value>A read-only span over <see cref="CharacterMapBytes"/>.</value>
        /// <remarks>
        /// Indexed by raw line byte value; each table entry is a bitmask of eligible encoding line types
        /// (<see cref="Uue"/>, <see cref="B64"/>, <see cref="Bhx"/>).
        /// </remarks>
        private static ReadOnlySpan<byte> CharacterMap => CharacterMapBytes;

        /// <summary>
        /// Diablo <c>arttype.c</c> character classification table (256 entries) for body line encoding detection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each index is an ASCII (or extended) byte value from a candidate body line. The stored value is a bitmask
        /// OR-ed into running <c>isType</c> / <c>isNotType</c> accumulators in <see cref="ClassifyLineAsTypes"/>.
        /// </para>
        /// <para>Immutable after static initialization; never mutated at runtime.</para>
        /// </remarks>
        private static readonly byte[] CharacterMapBytes =
        [
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            1, 5, 5, 5, 5, 5, 5, 7, 5, 5, 5, 7, 5, 1, 3, 7,
            7, 7, 7, 7, 7, 7, 3, 7, 7, 1, 1, 1, 3, 1, 1, 5,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 3, 7, 7,
            7, 7, 7, 7, 5, 1, 1, 1, 1, 5, 0, 6, 6, 6, 6, 6,
            2, 6, 6, 6, 6, 6, 6, 2, 6, 6, 6, 6, 2, 2, 2, 6,
            6, 6, 2, 2, 2, 2, 2, 2, 2, 2, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        ];

        /// <summary>
        /// Classifies an article buffer into <see cref="ArticleTypeFlags"/> by scanning all headers and up to
        /// <see cref="MaxClassificationBytes"/> of body content.
        /// </summary>
        /// <param name="articleBytes">
        /// Full article octets including headers, optional header/body separator, and optional body. May be empty.
        /// </param>
        /// <returns>
        /// Accumulated classification flags for the scanned prefix. Returns <see cref="ArticleTypeFlags.Default"/> when
        /// <paramref name="articleBytes"/> is empty or no non-default signals are detected.
        /// </returns>
        /// <remarks>
        /// <para><b>Scan flow:</b></para>
        /// <list type="number">
        /// <item>
        /// <description>
        /// Locate the header/body separator with <see cref="ArticleByteScanSimd.FindHeaderSeparator"/>. When no separator
        /// exists, the entire buffer is treated as header content.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Walk physical lines with <see cref="ArticleByteScanSimd.IndexOfLineFeed"/>, stripping a trailing <c>\r</c>
        /// before CRLF terminators.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Feed each line to <see cref="Scanner.ProcessLine"/> with an <c>inHeader</c> flag derived from line offset vs
        /// separator.
        /// </description>
        /// </item>
        /// <item><description>Run <see cref="Scanner.CompleteClassification"/> for finalize-derived flags.</description></item>
        /// </list>
        /// <para>
        /// Body iteration stops early when <see cref="BodyEncodingIdentified"/> bits are already set on the scanner, even
        /// if additional body bytes remain within <see cref="MaxClassificationBytes"/>.
        /// </para>
        /// <para>Never throws for in-bounds article buffers.</para>
        /// </remarks>
        public static ArticleTypeFlags Classify(ReadOnlySpan<byte> articleBytes)
        {
            Scanner scanner = new();
            scanner.Reset();

            if (articleBytes.Length == 0)
            {
                return ArticleTypeFlags.Default;
            }

            (int headerEnd, int bodyStart) = ArticleByteScanSimd.FindHeaderSeparator(articleBytes);
            int scanLimit = bodyStart < 0
                ? articleBytes.Length
                : Math.Min(articleBytes.Length, bodyStart + MaxClassificationBytes);

            int index = 0;
            while (index < scanLimit)
            {
                bool inHeader = headerEnd < 0 || index < headerEnd;
                if (!inHeader && (scanner.Type & BodyEncodingIdentified) != 0)
                {
                    break;
                }

                int lineEnd = ArticleByteScanSimd.IndexOfLineFeed(articleBytes, index, scanLimit);

                int contentEnd = lineEnd;
                if (contentEnd > index && articleBytes[contentEnd - 1] == (byte)'\r')
                {
                    contentEnd--;
                }

                ReadOnlySpan<byte> line = articleBytes[index..contentEnd];
                scanner.ProcessLine(line, inHeader);

                index = lineEnd + 1;
            }

            scanner.CompleteClassification();
            return scanner.Type;
        }

        /// <summary>
        /// Per-article mutable scan state for <see cref="Classify"/>; accumulates <see cref="ArticleTypeFlags"/> and
        /// cross-header working data.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Instantiated on the stack for each <see cref="Classify"/> call. Not thread-safe; one scanner per invocation.
        /// </para>
        /// <para>
        /// Header detection uses first-character shortcuts where safe (<c>a</c> for <c>Approved:</c>, <c>c</c>+<c>o</c> for
        /// <c>Content-*</c> / <c>Control:</c>, and so on) before broader prefix tests.
        /// </para>
        /// </remarks>
        private sealed class Scanner
        {
            /// <summary>
            /// Accumulated classification flags for the current article scan.
            /// </summary>
            /// <remarks>
            /// Starts as <see cref="ArticleTypeFlags.Default"/> on <see cref="Reset"/>. <see cref="ClearDefaultIfNeeded"/>
            /// removes <see cref="ArticleTypeFlags.Default"/> once any other bit is set.
            /// </remarks>
            internal ArticleTypeFlags Type { get; private set; } = ArticleTypeFlags.Default;

            /// <summary>
            /// Running count of consecutive UU-classified body lines (reset when another encoding line type matches).
            /// </summary>
            /// <remarks>
            /// <see cref="ArticleTypeFlags.UuEncode"/> and <see cref="ArticleTypeFlags.Binary"/> are OR-ed when this exceeds
            /// <c>8</c> in <see cref="ProcessBodyLine"/>.
            /// </remarks>
            private int _uuEncode;

            /// <summary>
            /// Running count of consecutive BinHex-classified body lines or priming state from the BinHex 4.0 banner.
            /// </summary>
            /// <remarks>
            /// Primed to <c>1</c> by <see cref="TryDetectBinHexBanner"/> or <c>application/mac-binhex40</c> content type.
            /// <see cref="ArticleTypeFlags.BinHex"/> is OR-ed when the running count exceeds <c>8</c>.
            /// </remarks>
            private int _binHex;

            /// <summary>
            /// Running count of consecutive Base64-classified body lines (reset when another encoding line type matches).
            /// </summary>
            /// <remarks>
            /// <see cref="ArticleTypeFlags.Base64"/> and <see cref="ArticleTypeFlags.Binary"/> are OR-ed when this exceeds
            /// <c>8</c> in <see cref="ProcessBodyLine"/>.
            /// </remarks>
            private int _base64;

            /// <summary>
            /// Comma-separated newsgroup token count from the last <c>Newsgroups:</c> header line observed.
            /// </summary>
            /// <remarks>
            /// Recomputed on each matching header line; used only at <see cref="CompleteClassification"/> for
            /// <see cref="ArticleTypeFlags.MassCrosspost"/>.
            /// </remarks>
            private int _newsgroupCount;

            /// <summary>
            /// Heap copy of the trimmed <c>Newsgroups:</c> value for finalize comparison.
            /// </summary>
            /// <remarks>
            /// <see langword="null"/> until a <c>Newsgroups:</c> line is seen. Allocated via
            /// <see cref="HeaderValueUtilities.CopyHeaderValue"/>.
            /// </remarks>
            private byte[]? _newsgroupsValue;

            /// <summary>
            /// Heap copy of the trimmed <c>Followup-To:</c> value for finalize comparison.
            /// </summary>
            /// <remarks>
            /// <see langword="null"/> until a <c>Followup-To:</c> line is seen. Allocated via
            /// <see cref="HeaderValueUtilities.CopyHeaderValue"/>.
            /// </remarks>
            private byte[]? _followupToValue;

            /// <summary>
            /// Whether a <c>Followup-To:</c> header line was observed (even when the trimmed value is empty).
            /// </summary>
            private bool _hasFollowupTo;

            /// <summary>
            /// Resets all scan accumulators to their initial state for a new <see cref="Classify"/> invocation.
            /// </summary>
            /// <remarks>Called once at the start of <see cref="Classify"/> before line iteration.</remarks>
            internal void Reset()
            {
                Type = ArticleTypeFlags.Default;
                _uuEncode = 0;
                _binHex = 0;
                _base64 = 0;
                _newsgroupCount = 0;
                _newsgroupsValue = null;
                _followupToValue = null;
                _hasFollowupTo = false;
            }

            /// <summary>
            /// Applies finalize-derived flags that require full-header context after line scanning completes.
            /// </summary>
            /// <remarks>
            /// <para><b>Derived flags:</b></para>
            /// <list type="bullet">
            /// <item>
            /// <description>
            /// <see cref="ArticleTypeFlags.MassCrosspost"/> when <see cref="_newsgroupCount"/> &gt;=
            /// <see cref="MassCrosspostThreshold"/>.
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// <see cref="ArticleTypeFlags.FollowupRedirect"/> when <c>Newsgroups</c> and non-empty <c>Followup-To</c>
            /// differ per <see cref="HeaderValueUtilities.EqualsAsciiIgnoreCaseTrimmed"/>.
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// <see cref="ArticleTypeFlags.SignedControl"/> when <see cref="ArticleTypeFlags.Control"/>,
            /// <see cref="ArticleTypeFlags.Approved"/>, and (<see cref="ArticleTypeFlags.PgpSigned"/> or
            /// <see cref="ArticleTypeFlags.Smime"/>) are all present.
            /// </description>
            /// </item>
            /// </list>
            /// <para>Ends with <see cref="ClearDefaultIfNeeded"/>.</para>
            /// </remarks>
            internal void CompleteClassification()
            {
                if (_newsgroupCount >= MassCrosspostThreshold)
                {
                    Type |= ArticleTypeFlags.MassCrosspost;
                }

                if (_newsgroupsValue is not null &&
                    _hasFollowupTo &&
                    _followupToValue is not null &&
                    _followupToValue.Length > 0 &&
                    !HeaderValueUtilities.EqualsAsciiIgnoreCaseTrimmed(_newsgroupsValue, _followupToValue))
                {
                    Type |= ArticleTypeFlags.FollowupRedirect;
                }

                if ((Type & ArticleTypeFlags.Control) != 0 &&
                    (Type & ArticleTypeFlags.Approved) != 0 &&
                    ((Type & ArticleTypeFlags.PgpSigned) != 0 || (Type & ArticleTypeFlags.Smime) != 0))
                {
                    Type |= ArticleTypeFlags.SignedControl;
                }

                ClearDefaultIfNeeded();
            }

            /// <summary>
            /// Classifies one physical header or body line and updates <see cref="Type"/> and encoding counters.
            /// </summary>
            /// <param name="line">Line bytes without line terminators. Empty lines are ignored after default clearing.</param>
            /// <param name="inHeader">
            /// <see langword="true"/> when the line lies before the header/body separator; otherwise body phase.
            /// </param>
            /// <remarks>
            /// <para>
            /// Always runs <see cref="TryDetectYEnc"/> and <see cref="TryDetectBinHexBanner"/> before branching. Body lines
            /// are skipped when <see cref="BodyEncodingIdentified"/> is already set on <see cref="Type"/>.
            /// </para>
            /// </remarks>
            internal void ProcessLine(ReadOnlySpan<byte> line, bool inHeader)
            {
                ClearDefaultIfNeeded();

                if (line.Length == 0)
                {
                    return;
                }

                TryDetectYEnc(line);
                TryDetectBinHexBanner(line);

                if (inHeader)
                {
                    ProcessHeaderLine(line);
                    return;
                }

                if ((Type & BodyEncodingIdentified) != 0)
                {
                    return;
                }

                ProcessBodyLine(line);
            }

            /// <summary>
            /// Classifies a single header-phase line into operational and MIME-related <see cref="ArticleTypeFlags"/>.
            /// </summary>
            /// <param name="line">Header line bytes without terminators.</param>
            /// <remarks>
            /// <para>
            /// Uses first-byte (and second-byte for <c>c</c> lines) shortcuts before prefix tests. <c>Content-Type</c>,
            /// <c>Content-transfer-encoding</c>, and <c>Control</c> lines share the <c>c</c>+<c>o</c> gate and route through
            /// <see cref="ProcessContentTypeHeader"/> before control-specific checks.
            /// </para>
            /// <para>
            /// <c>Newsgroups:</c> and <c>Followup-To:</c> values are copied to the heap for finalize comparisons; repeated
            /// headers overwrite prior values (last wins).
            /// </para>
            /// </remarks>
            private void ProcessHeaderLine(ReadOnlySpan<byte> line)
            {
                byte first = ArticleByteScanSimd.ToLowerAscii(line[0]);

                if (first == (byte)'a' &&
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Approved:"u8))
                {
                    Type |= ArticleTypeFlags.Approved;
                }

                if (first == (byte)'s' &&
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Supersedes:"u8))
                {
                    Type |= ArticleTypeFlags.Supersedes;
                }

                if (first == (byte)'n' &&
                    HeaderValueUtilities.TryGetHeaderValue(line, "Newsgroups:"u8, out ReadOnlySpan<byte> newsgroupsValue))
                {
                    _newsgroupCount = HeaderValueUtilities.CountCommaSeparatedTokens(newsgroupsValue);
                    _newsgroupsValue = HeaderValueUtilities.CopyHeaderValue(newsgroupsValue);
                }

                if (first == (byte)'f' &&
                    HeaderValueUtilities.TryGetHeaderValue(line, "Followup-To:"u8, out ReadOnlySpan<byte> followupValue))
                {
                    _hasFollowupTo = true;
                    _followupToValue = HeaderValueUtilities.CopyHeaderValue(followupValue);
                }

                if (first == (byte)'x' &&
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "X-Newsposter:"u8))
                {
                    TrySetNzbGenerated(line);
                }

                if (first == (byte)'u' &&
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "User-Agent:"u8))
                {
                    TrySetNzbGenerated(line);
                }

                if (first == (byte)'c' && line.Length > 1 && ArticleByteScanSimd.ToLowerAscii(line[1]) == (byte)'o')
                {
                    ProcessContentTypeHeader(line);

                    if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Control: "u8))
                    {
                        Type |= ArticleTypeFlags.Control;
                    }

                    if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Control: cancel "u8))
                    {
                        Type |= ArticleTypeFlags.Cancel;
                    }
                }

                if (first == (byte)'m' && ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Mime-Version: "u8))
                {
                    Type |= ArticleTypeFlags.Mime;
                }
            }

            /// <summary>
            /// Detects yEnc section headers on any physical line and sets yEnc-related flags.
            /// </summary>
            /// <param name="line">Candidate line bytes (header or body).</param>
            /// <remarks>
            /// <para>
            /// Requires a leading <c>=</c> (case-insensitive). <c>=ybegin part=</c> sets
            /// <see cref="ArticleTypeFlags.Binary"/>, <see cref="ArticleTypeFlags.Partial"/>, and
            /// <see cref="ArticleTypeFlags.YEnc"/>. <c>=ybegin line=</c> sets <see cref="ArticleTypeFlags.Binary"/> and
            /// <see cref="ArticleTypeFlags.YEnc"/> only.
            /// </para>
            /// <para>Both patterns may match the same line when prefixes overlap; flags are OR-combined.</para>
            /// </remarks>
            private void TryDetectYEnc(ReadOnlySpan<byte> line)
            {
                if (line.Length == 0 || ArticleByteScanSimd.ToLowerAscii(line[0]) != (byte)'=')
                {
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "=ybegin part="u8))
                {
                    Type |= ArticleTypeFlags.Binary | ArticleTypeFlags.Partial | ArticleTypeFlags.YEnc;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "=ybegin line="u8))
                {
                    Type |= ArticleTypeFlags.Binary | ArticleTypeFlags.YEnc;
                }
            }

            /// <summary>
            /// Primes BinHex body line counting when the Diablo BinHex 4.0 banner line is seen.
            /// </summary>
            /// <param name="line">Candidate line bytes (header or body).</param>
            /// <remarks>
            /// Sets <see cref="_binHex"/> to <c>1</c> when the line begins with
            /// <c>(This file must be converted with BinHex 4.0)</c> (case-insensitive). Does not set
            /// <see cref="ArticleTypeFlags.BinHex"/> until consecutive BinHex-width body lines exceed eight in
            /// <see cref="ProcessBodyLine"/>.
            /// </remarks>
            private void TryDetectBinHexBanner(ReadOnlySpan<byte> line)
            {
                if (line.Length > 0 &&
                    line[0] == (byte)'(' &&
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "(This file must be converted with BinHex 4.0)"u8))
                {
                    _binHex = 1;
                }
            }

            /// <summary>
            /// Maps <c>Content-Type</c>, <c>Content-transfer-encoding</c>, and related <c>Content-*</c> header lines to MIME flags.
            /// </summary>
            /// <param name="line">Header line expected to begin with <c>Content-</c> (case-insensitive).</param>
            /// <remarks>
            /// <para>
            /// More specific <c>Content-Type:</c> subtypes are tested before generic prefixes. The final
            /// <c>Content-Type: </c> fallback sets <see cref="ArticleTypeFlags.Mime"/> only.
            /// </para>
            /// <para>See <see cref="ArticleTypeFlags"/> remarks for the full flag combination table per subtype.</para>
            /// </remarks>
            private void ProcessContentTypeHeader(ReadOnlySpan<byte> line)
            {
                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: text/html"u8))
                {
                    Type |= ArticleTypeFlags.Html | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: text/plain"u8))
                {
                    Type |= ArticleTypeFlags.Text | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: multipart/mixed"u8))
                {
                    Type |= ArticleTypeFlags.MultipartMixed | ArticleTypeFlags.Multipart | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: multipart/alternative"u8))
                {
                    Type |= ArticleTypeFlags.MultipartAlternative | ArticleTypeFlags.Multipart | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: multipart/related"u8))
                {
                    Type |= ArticleTypeFlags.MultipartRelated | ArticleTypeFlags.Multipart | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: multipart/signed"u8))
                {
                    Type |= ArticleTypeFlags.MultipartSigned | ArticleTypeFlags.Multipart | ArticleTypeFlags.Mime | ArticleTypeFlags.PgpSigned;
                    if (ArticleByteScanSimd.ContainsAsciiIgnoreCase(line, "application/pkcs7-signature"u8))
                    {
                        Type |= ArticleTypeFlags.Smime;
                    }

                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: multipart"u8))
                {
                    Type |= ArticleTypeFlags.Multipart | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: application/zip"u8) ||
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: application/x-rar"u8) ||
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: application/x-7z-compressed"u8))
                {
                    Type |= ArticleTypeFlags.Archive | ArticleTypeFlags.Binary | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: application/pkcs7-mime"u8) ||
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: application/x-pkcs7-mime"u8))
                {
                    Type |= ArticleTypeFlags.Smime | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: image/"u8))
                {
                    Type |= ArticleTypeFlags.Image | ArticleTypeFlags.Binary | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: video/"u8))
                {
                    Type |= ArticleTypeFlags.Video | ArticleTypeFlags.Binary | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: audio/"u8))
                {
                    Type |= ArticleTypeFlags.Audio | ArticleTypeFlags.Binary | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-transfer-encoding: base64"u8))
                {
                    Type |= ArticleTypeFlags.Base64 | ArticleTypeFlags.Binary;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: application/mac-binhex40"u8))
                {
                    _binHex = 1;
                    Type |= ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: application/octet-stream"u8))
                {
                    Type |= ArticleTypeFlags.Binary | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: message/partial"u8))
                {
                    Type |= ArticleTypeFlags.Partial | ArticleTypeFlags.Mime;
                    return;
                }

                if (ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: "u8))
                {
                    Type |= ArticleTypeFlags.Mime;
                }
            }

            /// <summary>
            /// Sets <see cref="ArticleTypeFlags.NzbGenerated"/> when a known NZB poster hint appears in the line.
            /// </summary>
            /// <param name="line">
            /// Full <c>X-Newsposter:</c> or <c>User-Agent:</c> header line bytes (including field name).
            /// </param>
            /// <remarks>
            /// Iterates <see cref="NzbPosterHintBytes"/> in definition order; stops at the first case-insensitive substring
            /// match via <see cref="ArticleByteScanSimd.ContainsAsciiIgnoreCase"/>.
            /// </remarks>
            private void TrySetNzbGenerated(ReadOnlySpan<byte> line)
            {
                foreach (byte[] hint in NzbPosterHintBytes)
                {
                    if (ArticleByteScanSimd.ContainsAsciiIgnoreCase(line, hint))
                    {
                        Type |= ArticleTypeFlags.NzbGenerated;
                        return;
                    }
                }
            }

            /// <summary>
            /// Classifies one body line for encoding types, PGP armor, and consecutive encoding counters.
            /// </summary>
            /// <param name="line">Body line bytes without terminators.</param>
            /// <remarks>
            /// <para>
            /// Strips Usenet quoted-printable style <c>&gt;</c> and optional <c>&gt; </c> prefixes before tests. PGP signature
            /// armor is detected before <see cref="ClassifyLineAsTypes"/>; PGP message armor is detected only on lines that are
            /// not classified as UU, BinHex, or Base64 payload lines.
            /// </para>
            /// <para>
            /// UU, BinHex, and Base64 counters are mutually exclusive per line; exceeding eight consecutive qualifying lines
            /// sets the corresponding encoding flags and <see cref="ArticleTypeFlags.Binary"/>.
            /// </para>
            /// </remarks>
            private void ProcessBodyLine(ReadOnlySpan<byte> line)
            {
                ReadOnlySpan<byte> bodyLine = line;
                if (bodyLine.Length > 0 && bodyLine[0] == (byte)'>')
                {
                    bodyLine = bodyLine[1..];
                    if (bodyLine.Length > 0 && bodyLine[0] == (byte)' ')
                    {
                        bodyLine = bodyLine[1..];
                    }
                }

                if (bodyLine.Length >= 27 &&
                    bodyLine[0] == (byte)'-' &&
                    bodyLine.StartsWith("-----BEGIN PGP SIGNATURE-----"u8))
                {
                    Type |= ArticleTypeFlags.PgpSigned;
                }

                int lineType = ClassifyLineAsTypes(bodyLine);
                if ((lineType & Uue) != 0)
                {
                    _base64 = 0;
                    _binHex = 0;
                    _uuEncode++;
                    if (_uuEncode > 8)
                    {
                        Type |= ArticleTypeFlags.UuEncode | ArticleTypeFlags.Binary;
                    }
                }
                else if ((lineType & Bhx) != 0)
                {
                    _uuEncode = 0;
                    _base64 = 0;
                    _binHex++;
                    if (_binHex > 8)
                    {
                        Type |= ArticleTypeFlags.BinHex | ArticleTypeFlags.Binary;
                    }
                }
                else if ((lineType & B64) != 0)
                {
                    _uuEncode = 0;
                    _binHex = 0;
                    _base64++;
                    if (_base64 > 8)
                    {
                        Type |= ArticleTypeFlags.Base64 | ArticleTypeFlags.Binary;
                    }
                }
                else
                {
                    if (bodyLine.Length >= 27 &&
                        bodyLine[0] == (byte)'-' &&
                        bodyLine.StartsWith("-----BEGIN PGP MESSAGE-----"u8))
                    {
                        Type |= ArticleTypeFlags.PgpMessage;
                    }

                    _uuEncode = 0;
                    _base64 = 0;
                    _binHex = 0;
                }

                ClearDefaultIfNeeded();
            }

            /// <summary>
            /// Clears <see cref="ArticleTypeFlags.Default"/> from <see cref="Type"/> once any other classification bit is set.
            /// </summary>
            /// <remarks>
            /// Invoked after most line processing paths and at the end of <see cref="CompleteClassification"/>. Does not
            /// remove <see cref="ArticleTypeFlags.Default"/> when <see cref="Type"/> is exactly
            /// <see cref="ArticleTypeFlags.Default"/>.
            /// </remarks>
            private void ClearDefaultIfNeeded()
            {
                if (Type != ArticleTypeFlags.Default)
                {
                    Type &= ~ArticleTypeFlags.Default;
                }
            }
        }

        /// <summary>
        /// Diablo <c>classifyLineAsTypes</c> port: tests whether a body line matches UU, Base64, or BinHex width/alphabet rules.
        /// </summary>
        /// <param name="line">Body line bytes after quoted-prefix stripping.</param>
        /// <returns>
        /// A bitmask of <see cref="Uue"/>, <see cref="B64"/>, and/or <see cref="Bhx"/> when the line qualifies; otherwise
        /// <c>0</c>.
        /// </returns>
        /// <remarks>
        /// <para><b>Length gates (Diablo-aligned):</b></para>
        /// <list type="bullet">
        /// <item><description>UU — first byte <c>M</c>, length exactly <c>61</c>.</description></item>
        /// <item><description>Base64 — length between <c>60</c> and <c>77</c> inclusive.</description></item>
        /// <item><description>BinHex — length <c>64</c> or <c>65</c>.</description></item>
        /// </list>
        /// <para>
        /// Each byte is classified through <see cref="CharacterMap"/>; when no encoding remains possible mid-line, returns
        /// <c>0</c> early. Never throws for in-bounds spans.
        /// </para>
        /// </remarks>
        private static int ClassifyLineAsTypes(ReadOnlySpan<byte> line)
        {
            int len = line.Length;
            if (len == 0)
            {
                return 0;
            }

            int isType = 0;
            int isNotType = 0xff;

            if (line[0] != (byte)'M' || len != 61)
            {
                isNotType &= ~Uue;
            }

            if (len is < 60 or > 77)
            {
                isNotType &= ~B64;
            }

            if (len is < 64 or > 65)
            {
                isNotType &= ~Bhx;
            }

            for (int i = 0; i < len; i++)
            {
                if ((isNotType & AllLineTypes) == 0)
                {
                    return 0;
                }

                byte map = CharacterMap[line[i]];
                isType |= map;
                isNotType &= map;
            }

            return isType & isNotType;
        }

        /// <summary>
        /// Builds the static <see cref="NzbPosterHintBytes"/> table from <see cref="NzbPosterHints"/> at type initialization.
        /// </summary>
        /// <returns>
        /// An array parallel to <see cref="NzbPosterHints"/> containing ASCII-encoded hint bytes for each string entry.
        /// </returns>
        /// <remarks>
        /// Called once to initialize <see cref="NzbPosterHintBytes"/>. Uses <see cref="Encoding.ASCII"/>; hints are expected to
        /// be ASCII-only poster names.
        /// </remarks>
        private static byte[][] CreateNzbPosterHintBytes()
        {
            byte[][] result = new byte[NzbPosterHints.Length][];
            for (int i = 0; i < NzbPosterHints.Length; i++)
            {
                result[i] = Encoding.ASCII.GetBytes(NzbPosterHints[i]);
            }

            return result;
        }
    }
}
