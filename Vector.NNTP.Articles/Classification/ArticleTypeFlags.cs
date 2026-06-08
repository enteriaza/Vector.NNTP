// <copyright file="ArticleTypeFlags.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: Diablo arttype.c-aligned content classification bit flags accumulated per article during spool postprocessing.

namespace Vector.NNTP.Articles.Classification
{
    /// <summary>
    /// Diablo-style article content and control classification flags accumulated during header and body scanning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Producer:</b> <see cref="ArticleTypeClassifier.Classify"/> OR-accumulates these flags while scanning an article
    /// prefix (full header block plus up to <see cref="ArticleTypeClassifier.MaxClassificationBytes"/> of body). The
    /// layout mirrors Diablo <c>ARTTYPE_*</c> constants from
    /// <see href="https://github.com/jpmens/diablo/blob/master/lib/arttype.c">arttype.c</see> and extends them at bits
    /// 12–30 for operational headers, MIME taxonomy, and Usenet-specific signals.
    /// </para>
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Processing.ArticleSpoolPostprocessor"/> — yEnc CRC gate, SpamAssassin size gate, and
    /// <see cref="Processing.ArticleSpoolPostprocessResult.ArticleType"/> on success.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleTypes"/> via <see cref="Metrics.ArticleTypeMetricsTags"/> — one
    /// <c>article_type_total</c> increment per mapped bit on accepted articles.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Storage.NntpSpoolWriterPump"/> — cancel-processed INN news line when <see cref="Cancel"/> is set
    /// (article still accepted on the spool path).
    /// </description>
    /// </item>
    /// </list>
    /// <para><b>Spool-path routing:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="YEnc"/> — <see cref="Processing.ArticleSpoolPostprocessor"/> runs
    /// <see cref="Filters.YEnc.YEncSectionCrc.Validate"/> on the body and rejects the article on CRC failure.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Non-yEnc articles under the spam size gate may be sent to SpamAssassin via a temporary scan copy; other flags
    /// inform logging, metrics, and future policy but do not alone trigger discard on the current spool path.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Multiple flags may be set on one article (for example <see cref="Binary"/> | <see cref="YEnc"/>). The classifier
    /// clears <see cref="Default"/> on the first non-default detection. Body scanning terminates early only after a
    /// concrete encoding (<see cref="YEnc"/>, <see cref="UuEncode"/>, <see cref="Base64"/>, or <see cref="BinHex"/>)
    /// is identified; generic <see cref="Binary"/> from MIME headers alone does not stop the body scan. The full header
    /// block is always classified. yEnc section headers (<c>=ybegin</c>) are detected on every physical line (header or
    /// body) before the header/body branch.
    /// </para>
    /// <para><b>Bit layout (bits 0–31, underlying <see cref="uint"/>):</b></para>
    /// <list type="table">
    /// <listheader><term>Bit</term><description>Flag</description></listheader>
    /// <item><term>0–11</term><description>Diablo-aligned MIME, binary, encoding, control, and PGP message bits.</description></item>
    /// <item><term>12–26</term><description>Extended header-derived and finalize-derived operational flags.</description></item>
    /// <item><term>27–28</term><description>Reserved (unused; not assigned by <see cref="ArticleTypeClassifier"/>).</description></item>
    /// <item><term>29–30</term><description><see cref="Text"/> and <see cref="SignedControl"/>.</description></item>
    /// <item><term>31</term><description>Reserved (unused).</description></item>
    /// </list>
    /// <para>
    /// <b>Metrics contract:</b> Each mapped flag has a stable snake_case OpenTelemetry <c>type</c> tag in
    /// <see cref="Metrics.ArticleTypeMetricsTags"/>. Unmapped reserved bits never increment counters.
    /// </para>
    /// </remarks>
    [Flags]
    internal enum ArticleTypeFlags : uint
    {
        /// <summary>
        /// No specific content or control type has been detected yet (Diablo <c>ARTTYPE_DEFAULT</c>).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit value:</b> <c>0</c> (no bit set).</para>
        /// <para>
        /// Returned for plain-text articles when the scanned prefix contains no MIME, binary, yEnc, or control markers.
        /// Cleared automatically when any other flag is OR-ed in by <see cref="ArticleTypeClassifier"/> via
        /// <c>ClearDefaultIfNeeded</c>.
        /// </para>
        /// <para>
        /// When this is the only semantic state at accept time, <see cref="Metrics.NntpSpoolMetrics.RecordArticleTypes"/>
        /// emits the <see cref="Metrics.ArticleTypeMetricsTags.DefaultTag"/> bucket once.
        /// </para>
        /// </remarks>
        Default = 0,

        /// <summary>
        /// MIME article indicated by <c>Content-Type:</c> or <c>Mime-Version:</c> headers.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 0 (<c>1 &lt;&lt; 0</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Set when any line begins with <c>Content-Type: </c> (case-insensitive) or, in the header phase only,
        /// <c>Mime-Version: </c>. Does not imply <see cref="Binary"/> by itself.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>mime</c>.</para>
        /// </remarks>
        Mime = 1 << 0,

        /// <summary>
        /// Binary payload indicated by MIME types, transfer encoding, encoded body lines, or yEnc section headers.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 1 (<c>1 &lt;&lt; 1</c>). <b>Detection:</b> header and body.</para>
        /// <para>
        /// Header paths include <c>application/octet-stream</c>, archive/image/video/audio MIME families,
        /// <c>Content-transfer-encoding: base64</c>, and yEnc <c>=ybegin</c> lines. Body paths require more than eight
        /// consecutive qualifying UU, Base64, or BinHex lines (see respective encoding flags) before this bit is set from
        /// body scanning alone.
        /// </para>
        /// <para>
        /// Does not alone terminate body scanning — only concrete encoding flags
        /// (<see cref="YEnc"/>, <see cref="UuEncode"/>, <see cref="Base64"/>, <see cref="BinHex"/>) stop the body scan
        /// early. May coexist with those encoding flags.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>binary</c>.</para>
        /// </remarks>
        Binary = 1 << 1,

        /// <summary>
        /// UU-encoded body content (more than eight consecutive qualifying UU lines).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 2 (<c>1 &lt;&lt; 2</c>). <b>Detection:</b> body phase.</para>
        /// <para>
        /// Body lines must match Diablo <c>classifyLineAsTypes</c> UU rules (leading <c>M</c>, length exactly 61). Also
        /// sets <see cref="Binary"/> when the ninth qualifying line is observed. Stops further body classification early.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>uuencode</c>.</para>
        /// </remarks>
        UuEncode = 1 << 2,

        /// <summary>
        /// Base64-encoded body lines or a <c>Content-transfer-encoding: base64</c> header.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 3 (<c>1 &lt;&lt; 3</c>). <b>Detection:</b> header and body.</para>
        /// <para>
        /// Header detection sets <see cref="Binary"/> immediately. Body detection requires more than eight consecutive
        /// lines in the Base64 length window (60–77 characters per Diablo). Stops further body classification early when
        /// set from body scanning.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>base64</c>.</para>
        /// </remarks>
        Base64 = 1 << 3,

        /// <summary>
        /// BinHex 4.0 body content or <c>application/mac-binhex40</c> content type.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 4 (<c>1 &lt;&lt; 4</c>). <b>Detection:</b> header and body.</para>
        /// <para>
        /// Primed by the BinHex 4.0 banner <c>(This file must be converted with BinHex 4.0)</c> or the
        /// <c>application/mac-binhex40</c> content type (the latter also sets <see cref="Mime"/>). <see cref="Binary"/>
        /// is set after more than eight consecutive BinHex-width body lines (64–65 characters). Stops further body
        /// classification early when set from body scanning.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>binhex</c>.</para>
        /// </remarks>
        BinHex = 1 << 4,

        /// <summary>
        /// yEnc section present (<c>=ybegin line=</c> or <c>=ybegin part=</c> in the scanned prefix).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 5 (<c>1 &lt;&lt; 5</c>). <b>Detection:</b> any physical line (header or body).</para>
        /// <para>
        /// Always OR-ed with <see cref="Binary"/>. Multipart yEnc parts (<c>=ybegin part=</c>) also set
        /// <see cref="Partial"/>. This flag gates yEnc CRC validation in
        /// <see cref="Processing.ArticleSpoolPostprocessor"/> and stops further body classification early.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>yenc</c>.</para>
        /// </remarks>
        YEnc = 1 << 5,

        /// <summary>
        /// Usenet control message (<c>Control: </c> header present in the header phase).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 6 (<c>1 &lt;&lt; 6</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Set when a line begins with <c>Control: </c> (case-insensitive, including the trailing space). Distinct from
        /// <see cref="Cancel"/>; cancel lines match this prefix first and therefore also set <see cref="Control"/>.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>control</c>.</para>
        /// </remarks>
        Control = 1 << 6,

        /// <summary>
        /// Cancel control message (<c>Control: cancel </c> header).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 7 (<c>1 &lt;&lt; 7</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Set when a line begins with <c>Control: cancel </c> (case-insensitive). Always implies <see cref="Control"/>
        /// is also set. Triggers a cancel-processed INN news log line in
        /// <see cref="Storage.NntpSpoolWriterPump"/>; does not reject the article on the spool path by itself.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>cancel</c>.</para>
        /// </remarks>
        Cancel = 1 << 7,

        /// <summary>
        /// Multipart MIME content (<c>Content-Type: multipart</c> prefix or a specific multipart subtype).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 8 (<c>1 &lt;&lt; 8</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Set by generic <c>Content-Type: multipart</c> or by any specific multipart subtype handler. Also sets
        /// <see cref="Mime"/> because those lines match the general <c>Content-Type: </c> prefix rule.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>multipart</c>.</para>
        /// </remarks>
        Multipart = 1 << 8,

        /// <summary>
        /// HTML body indicated by <c>Content-Type: text/html</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 9 (<c>1 &lt;&lt; 9</c>). <b>Detection:</b> header phase.</para>
        /// <para>Also sets <see cref="Mime"/> via the shared <c>Content-Type: </c> detection path.</para>
        /// <para><b>Metrics tag:</b> <c>html</c>.</para>
        /// </remarks>
        Html = 1 << 9,

        /// <summary>
        /// Partial message (<c>Content-Type: message/partial</c> or yEnc multipart <c>=ybegin part=</c>).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 10 (<c>1 &lt;&lt; 10</c>). <b>Detection:</b> header and any-line yEnc scan.</para>
        /// <para>
        /// <c>message/partial</c> sets <see cref="Mime"/> but not <see cref="Binary"/>. yEnc multipart detection also sets
        /// <see cref="YEnc"/> and <see cref="Binary"/>.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>partial</c>.</para>
        /// </remarks>
        Partial = 1 << 10,

        /// <summary>
        /// OpenPGP message armor detected in the body (<c>-----BEGIN PGP MESSAGE-----</c>).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 11 (<c>1 &lt;&lt; 11</c>). <b>Detection:</b> body phase.</para>
        /// <para>
        /// Evaluated only on body lines that are not classified as UU, Base64, or BinHex payload lines. Quoted-printable
        /// style <c>&gt;</c> / <c>&gt; </c> prefixes are stripped before the armor test. Does not set
        /// <see cref="Binary"/> by itself.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>pgp_message</c>.</para>
        /// </remarks>
        PgpMessage = 1 << 11,

        /// <summary>
        /// Moderated or approved control traffic indicated by an <c>Approved:</c> header.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 12 (<c>1 &lt;&lt; 12</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Header-phase detection only. Contributes to derived <see cref="SignedControl"/> when combined with
        /// <see cref="Control"/> and <see cref="PgpSigned"/> or <see cref="Smime"/>. Useful for moderated groups,
        /// metrics, and future policy.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>approved</c>.</para>
        /// </remarks>
        Approved = 1 << 12,

        /// <summary>
        /// Replace semantics indicated by a <c>Supersedes:</c> header.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 13 (<c>1 &lt;&lt; 13</c>). <b>Detection:</b> header phase.</para>
        /// <para>Header-phase detection only. Useful for replace workflows and future cancel/supersede policy.</para>
        /// <para><b>Metrics tag:</b> <c>supersedes</c>.</para>
        /// </remarks>
        Supersedes = 1 << 13,

        /// <summary>
        /// OpenPGP detached signature armor or <c>multipart/signed</c> MIME wrapper detected.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 14 (<c>1 &lt;&lt; 14</c>). <b>Detection:</b> header and body.</para>
        /// <para>
        /// Set from <c>Content-Type: multipart/signed</c> (also sets <see cref="MultipartSigned"/>,
        /// <see cref="Multipart"/>, and <see cref="Mime"/>) or from body armor <c>-----BEGIN PGP SIGNATURE-----</c> after
        /// quoted-line prefix stripping.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>pgp_signed</c>.</para>
        /// </remarks>
        PgpSigned = 1 << 14,

        /// <summary>
        /// S/MIME signed or enveloped content detected via PKCS#7 MIME types or parameters.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 15 (<c>1 &lt;&lt; 15</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Matches <c>Content-Type: application/pkcs7-mime</c>, <c>application/x-pkcs7-mime</c>, or
        /// <c>multipart/signed</c> lines that also contain an <c>application/pkcs7-signature</c> protocol token. Always
        /// sets <see cref="Mime"/>; <c>multipart/signed</c> paths also set <see cref="MultipartSigned"/>,
        /// <see cref="Multipart"/>, and typically <see cref="PgpSigned"/>.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>smime</c>.</para>
        /// </remarks>
        Smime = 1 << 15,

        /// <summary>
        /// <c>Content-Type: multipart/mixed</c> detected.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 16 (<c>1 &lt;&lt; 16</c>). <b>Detection:</b> header phase.</para>
        /// <para>Also sets <see cref="Mime"/> and <see cref="Multipart"/>.</para>
        /// <para><b>Metrics tag:</b> <c>multipart_mixed</c>.</para>
        /// </remarks>
        MultipartMixed = 1 << 16,

        /// <summary>
        /// <c>Content-Type: multipart/alternative</c> detected.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 17 (<c>1 &lt;&lt; 17</c>). <b>Detection:</b> header phase.</para>
        /// <para>Also sets <see cref="Mime"/> and <see cref="Multipart"/>.</para>
        /// <para><b>Metrics tag:</b> <c>multipart_alternative</c>.</para>
        /// </remarks>
        MultipartAlternative = 1 << 17,

        /// <summary>
        /// <c>Content-Type: multipart/related</c> detected.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 18 (<c>1 &lt;&lt; 18</c>). <b>Detection:</b> header phase.</para>
        /// <para>Also sets <see cref="Mime"/> and <see cref="Multipart"/>.</para>
        /// <para><b>Metrics tag:</b> <c>multipart_related</c>.</para>
        /// </remarks>
        MultipartRelated = 1 << 18,

        /// <summary>
        /// <c>Content-Type: multipart/signed</c> detected as a MIME subtype.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 19 (<c>1 &lt;&lt; 19</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Also sets <see cref="Mime"/>, <see cref="Multipart"/>, and <see cref="PgpSigned"/>. Sets
        /// <see cref="Smime"/> additionally when the line contains <c>application/pkcs7-signature</c>.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>multipart_signed</c>.</para>
        /// </remarks>
        MultipartSigned = 1 << 19,

        /// <summary>
        /// Archive MIME family detected (<c>application/zip</c>, RAR, or 7z content types).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 20 (<c>1 &lt;&lt; 20</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Matches <c>application/zip</c>, <c>application/x-rar</c>, or <c>application/x-7z-compressed</c>. Also sets
        /// <see cref="Binary"/> and <see cref="Mime"/>.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>archive</c>.</para>
        /// </remarks>
        Archive = 1 << 20,

        /// <summary>
        /// Image MIME family detected (<c>Content-Type: image/</c> prefix).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 21 (<c>1 &lt;&lt; 21</c>). <b>Detection:</b> header phase.</para>
        /// <para>Also sets <see cref="Binary"/> and <see cref="Mime"/>.</para>
        /// <para><b>Metrics tag:</b> <c>image</c>.</para>
        /// </remarks>
        Image = 1 << 21,

        /// <summary>
        /// Video MIME family detected (<c>Content-Type: video/</c> prefix).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 22 (<c>1 &lt;&lt; 22</c>). <b>Detection:</b> header phase.</para>
        /// <para>Also sets <see cref="Binary"/> and <see cref="Mime"/>.</para>
        /// <para><b>Metrics tag:</b> <c>video</c>.</para>
        /// </remarks>
        Video = 1 << 22,

        /// <summary>
        /// Audio MIME family detected (<c>Content-Type: audio/</c> prefix).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 23 (<c>1 &lt;&lt; 23</c>). <b>Detection:</b> header phase.</para>
        /// <para>Also sets <see cref="Binary"/> and <see cref="Mime"/>.</para>
        /// <para><b>Metrics tag:</b> <c>audio</c>.</para>
        /// </remarks>
        Audio = 1 << 23,

        /// <summary>
        /// Automated NZB-driven upload indicated by poster or user-agent hints.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 24 (<c>1 &lt;&lt; 24</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Matched case-insensitively on <c>X-Newsposter:</c> or <c>User-Agent:</c> against tokens in
        /// <see cref="ArticleTypeClassifier.NzbPosterHints"/> (for example <c>Nyuu</c>, <c>ngPost</c>,
        /// <c>PowerPost</c>).
        /// </para>
        /// <para><b>Metrics tag:</b> <c>nzb_generated</c>.</para>
        /// </remarks>
        NzbGenerated = 1 << 24,

        /// <summary>
        /// Large crosspost detected (<c>Newsgroups:</c> lists at least ten groups).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 25 (<c>1 &lt;&lt; 25</c>). <b>Detection:</b> finalize phase.</para>
        /// <para>
        /// Derived in <see cref="ArticleTypeClassifier"/> completion when comma-separated newsgroup token counting
        /// (<see cref="HeaderValueUtilities.CountCommaSeparatedTokens"/>) is greater than or equal to
        /// <see cref="ArticleTypeClassifier.MassCrosspostThreshold"/> (<c>10</c>).
        /// </para>
        /// <para><b>Metrics tag:</b> <c>mass_crosspost</c>.</para>
        /// </remarks>
        MassCrosspost = 1 << 25,

        /// <summary>
        /// Followup redirect detected when <c>Followup-To:</c> differs from <c>Newsgroups:</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 26 (<c>1 &lt;&lt; 26</c>). <b>Detection:</b> finalize phase.</para>
        /// <para>
        /// Derived in completion when both headers are present, <c>Followup-To</c> is non-empty after trim, and normalized
        /// values differ per <see cref="HeaderValueUtilities.EqualsAsciiIgnoreCaseTrimmed"/>.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>followup_redirect</c>.</para>
        /// </remarks>
        FollowupRedirect = 1 << 26,

        /// <summary>
        /// Plain text MIME family detected (<c>Content-Type: text/plain</c>).
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 29 (<c>1u &lt;&lt; 29</c>). <b>Detection:</b> header phase.</para>
        /// <para>
        /// Bits 27–28 remain reserved and unused between <see cref="FollowupRedirect"/> and this flag. Also sets
        /// <see cref="Mime"/>. Does not set <see cref="Binary"/> by itself.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>text</c>.</para>
        /// </remarks>
        Text = 1u << 29,

        /// <summary>
        /// Signed control message: <see cref="Control"/> with <see cref="Approved"/> and cryptographic signature markers.
        /// </summary>
        /// <remarks>
        /// <para><b>Bit:</b> 30 (<c>1u &lt;&lt; 30</c>). <b>Detection:</b> finalize phase (derived; never set directly by line scans).</para>
        /// <para>
        /// OR-ed in completion when <see cref="Control"/> and <see cref="Approved"/> are set together with
        /// <see cref="PgpSigned"/> or <see cref="Smime"/>. Metadata only — no spool rejection on the current path. Bit 31
        /// remains reserved.
        /// </para>
        /// <para><b>Metrics tag:</b> <c>signed_control</c>.</para>
        /// </remarks>
        SignedControl = 1u << 30,
    }
}
