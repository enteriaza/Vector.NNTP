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
    /// <para><b>Role:</b> Invoked by <see cref="Processing.ArticleSpoolPostprocessor"/> after header semantics pass and
    /// before yEnc CRC or SpamAssassin filtering. The returned <see cref="ArticleTypeFlags"/> drive spool-path decisions
    /// (yEnc validation vs spam scan gate), metrics, and future policy.</para>
    /// <para><b>Scan invariant:</b></para>
    /// <list type="bullet">
    /// <item><description><b>Header classification always scans the entire header block.</b></description></item>
    /// <item><description><b>Body classification may terminate early when a concrete encoding (<see cref="ArticleTypeFlags.YEnc"/>, <see cref="ArticleTypeFlags.UuEncode"/>, <see cref="ArticleTypeFlags.Base64"/>, or <see cref="ArticleTypeFlags.BinHex"/>) is identified</b> (or when <see cref="MaxClassificationBytes"/> is reached).</description></item>
    /// </list>
    /// <para><b>Scan scope:</b></para>
    /// <list type="number">
    /// <item><description>Classify every header line through the <c>\r\n\r\n</c> or <c>\n\n</c> separator (no header-size cap).</description></item>
    /// <item><description>Classify body lines after the separator until <see cref="MaxClassificationBytes"/> of body octets or early exit.</description></item>
    /// </list>
    /// <para><b>Performance:</b> Line and separator scans delegate to <see cref="Scanning.ArticleByteScanSimd"/>.</para>
    /// <para><b>Thread safety:</b> Stateless static methods; safe for concurrent writer pumps.</para>
    /// </remarks>
    public static class ArticleTypeClassifier
    {
        /// <summary>
        /// Maximum number of body bytes scanned for classification after the header/body separator (64 KiB).
        /// </summary>
        public const int MaxClassificationBytes = 65_536;

        /// <summary>
        /// Minimum <c>Newsgroups:</c> token count that sets <see cref="ArticleTypeFlags.MassCrosspost"/>.
        /// </summary>
        internal const int MassCrosspostThreshold = 10;

        /// <summary>
        /// Body-scan stops once any of these encoding flags is set (generic <see cref="ArticleTypeFlags.Binary"/> from MIME headers alone does not qualify).
        /// </summary>
        private const ArticleTypeFlags BodyEncodingIdentified =
            ArticleTypeFlags.YEnc |
            ArticleTypeFlags.UuEncode |
            ArticleTypeFlags.Base64 |
            ArticleTypeFlags.BinHex;

        /// <summary>
        /// Known automated NZB poster tokens matched case-insensitively in <c>X-Newsposter:</c> or <c>User-Agent:</c> headers.
        /// </summary>
        /// <remarks>
        /// Extend this list as new posting tools appear; matching uses <see cref="ArticleByteScanSimd.ContainsAsciiIgnoreCase"/>.
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
        /// ASCII byte forms of <see cref="NzbPosterHints"/> for allocation-free substring matching.
        /// </summary>
        private static readonly byte[][] NzbPosterHintBytes = CreateNzbPosterHintBytes();

        private const byte Uue = 0x01;
        private const byte B64 = 0x02;
        private const byte Bhx = 0x04;
        private const byte AllLineTypes = Uue | B64 | Bhx;

        private static ReadOnlySpan<byte> CharacterMap => _characterMap;

        private static readonly byte[] _characterMap =
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
        /// <param name="articleBytes">Full article octets including headers, separator, and optional body.</param>
        /// <returns>Accumulated classification flags for the scanned prefix.</returns>
        /// <remarks>
        /// Header lines are always fully classified. Body scanning stops when a concrete body encoding is identified or the
        /// body byte cap is reached.
        /// </remarks>
        public static ArticleTypeFlags Classify(ReadOnlySpan<byte> articleBytes)
        {
            var scanner = new Scanner();
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

                ReadOnlySpan<byte> line = articleBytes.Slice(index, contentEnd - index);
                scanner.ProcessLine(line, inHeader);

                index = lineEnd + 1;
            }

            scanner.CompleteClassification();
            return scanner.Type;
        }

        private sealed class Scanner
        {
            internal ArticleTypeFlags Type { get; private set; } = ArticleTypeFlags.Default;

            private int _uuEncode;
            private int _binHex;
            private int _base64;
            private int _newsgroupCount;
            private byte[]? _newsgroupsValue;
            private byte[]? _followupToValue;
            private bool _hasFollowupTo;

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

            private void TryDetectBinHexBanner(ReadOnlySpan<byte> line)
            {
                if (line.Length > 0 &&
                    line[0] == (byte)'(' &&
                    ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "(This file must be converted with BinHex 4.0)"u8))
                {
                    _binHex = 1;
                }
            }

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

            private void ClearDefaultIfNeeded()
            {
                if (Type != ArticleTypeFlags.Default)
                {
                    Type &= ~ArticleTypeFlags.Default;
                }
            }
        }

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

            if (len < 60 || len > 77)
            {
                isNotType &= ~B64;
            }

            if (len < 64 || len > 65)
            {
                isNotType &= ~Bhx;
            }

            for (int i = 0; i < len; i++)
            {
                if ((isNotType & AllLineTypes) == 0)
                {
                    return 0;
                }

                byte map = CharacterMap[(int)line[i]];
                isType |= map;
                isNotType &= map;
            }

            return isType & isNotType;
        }

        private static byte[][] CreateNzbPosterHintBytes()
        {
            var result = new byte[NzbPosterHints.Length][];
            for (int i = 0; i < NzbPosterHints.Length; i++)
            {
                result[i] = Encoding.ASCII.GetBytes(NzbPosterHints[i]);
            }

            return result;
        }
    }
}
