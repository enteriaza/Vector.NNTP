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
    /// <see cref="ArticleTypeClassifier"/> OR-accumulates these flags while scanning an article prefix. The layout
    /// mirrors Diablo <c>ARTTYPE_*</c> constants from
    /// <see href="https://github.com/jpmens/diablo/blob/master/lib/arttype.c">arttype.c</see> and extends them at bits
    /// 12–30 for operational headers, MIME taxonomy, and Usenet-specific signals.
    /// </para>
    /// <para><b>Spool-path routing:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="YEnc"/> — <see cref="Processing.ArticleSpoolPostprocessor"/> runs
    /// <see cref="Vector.NNTP.Filters.YEnc.YEncSectionCrc.Validate"/> on the body and rejects the article on CRC failure.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Articles without <see cref="YEnc"/> and under the spam size gate may be sent to SpamAssassin via a temporary scan
    /// copy; other flags inform logging, metrics, and future policy but do not alone trigger discard on the current spool path.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Multiple flags may be set on one article (for example <see cref="Binary"/> | <see cref="YEnc"/>). The classifier
    /// clears <see cref="Default"/> on the first non-default detection. Body scanning terminates early only after a
    /// concrete encoding (<see cref="YEnc"/>, <see cref="UuEncode"/>, <see cref="Base64"/>, or <see cref="BinHex"/>)
    /// is identified; generic <see cref="Binary"/> from MIME headers alone does not stop the body scan. The full header
    /// block is always classified.
    /// </para>
    /// <para>
    /// <b>Underlying type:</b> Stored as <see cref="uint"/> so up to 32 independent bits are available on all supported
    /// platforms without sign-extension surprises in hot-path bitwise tests.
    /// </para>
    /// <para><b>Expansion headroom:</b> Bit 31 remains reserved. Bits 27–28 are intentionally unused (reserved for future
    /// policy layers that consume these flags).</para>
    /// </remarks>
    [Flags]
    public enum ArticleTypeFlags : uint
    {
        /// <summary>
        /// No specific content or control type has been detected yet (Diablo <c>ARTTYPE_DEFAULT</c>).
        /// </summary>
        /// <remarks>
        /// Returned for plain text articles when the scanned prefix contains no MIME, binary, yEnc, or control markers.
        /// Cleared automatically when any other flag is OR-ed in by <see cref="ArticleTypeClassifier"/>.
        /// </remarks>
        Default = 0,

        /// <summary>
        /// MIME article indicated by <c>Content-Type:</c> or <c>Mime-Version:</c> headers.
        /// </summary>
        /// <remarks>
        /// Set when any line begins with <c>Content-Type: </c> (case-insensitive) or, in the header phase only,
        /// <c>Mime-Version: </c>. Does not imply <see cref="Binary"/> by itself.
        /// </remarks>
        Mime = 1 << 0,

        /// <summary>
        /// Binary payload detected via content type, transfer encoding, encoded body lines, or yEnc section headers.
        /// </summary>
        /// <remarks>
        /// Set when content type, transfer encoding, encoded body lines, or yEnc section headers indicate binary payload.
        /// Does not alone terminate body scanning — only concrete encoding flags
        /// (<see cref="YEnc"/>, <see cref="UuEncode"/>, <see cref="Base64"/>, <see cref="BinHex"/>) stop the body scan
        /// early. May coexist with those encoding flags.
        /// </remarks>
        Binary = 1 << 1,

        /// <summary>
        /// UU-encoded body content (more than eight consecutive qualifying UU lines).
        /// </summary>
        /// <remarks>
        /// Body lines must match Diablo <c>classifyLineAsTypes</c> UU rules (leading <c>M</c>, length 61). Also sets
        /// <see cref="Binary"/> when the ninth qualifying line is observed.
        /// </remarks>
        UuEncode = 1 << 2,

        /// <summary>
        /// Base64-encoded body lines or a <c>Content-transfer-encoding: base64</c> header.
        /// </summary>
        /// <remarks>
        /// Header detection sets <see cref="Binary"/> immediately. Body detection requires more than eight consecutive
        /// lines in the Base64 length window (60–77 characters per Diablo).
        /// </remarks>
        Base64 = 1 << 3,

        /// <summary>
        /// BinHex 4.0 body content or <c>application/mac-binhex40</c> content type.
        /// </summary>
        /// <remarks>
        /// Primed by the BinHex 4.0 banner or the mac-binhex40 content type. <see cref="Binary"/> is set after more than
        /// eight consecutive BinHex-width body lines (64–65 characters).
        /// </remarks>
        BinHex = 1 << 4,

        /// <summary>
        /// yEnc section present (<c>=ybegin line=</c> or <c>=ybegin part=</c> in the scanned prefix).
        /// </summary>
        /// <remarks>
        /// Always OR-ed with <see cref="Binary"/>. Multipart yEnc parts also set <see cref="Partial"/>. This flag gates
        /// yEnc CRC validation in <see cref="Processing.ArticleSpoolPostprocessor"/>.
        /// </remarks>
        YEnc = 1 << 5,

        /// <summary>
        /// Usenet control message (<c>Control:</c> header present in the header phase).
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="Cancel"/>; any <c>Control: </c> line sets this flag.
        /// </remarks>
        Control = 1 << 6,

        /// <summary>
        /// Cancel control message (<c>Control: cancel </c> header).
        /// </summary>
        /// <remarks>
        /// Implies <see cref="Control"/> is also set.
        /// </remarks>
        Cancel = 1 << 7,

        /// <summary>
        /// Multipart MIME content (<c>Content-Type: multipart</c>).
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Mime"/> because the line matches the general <c>Content-Type: </c> prefix rule.
        /// </remarks>
        Multipart = 1 << 8,

        /// <summary>
        /// HTML body indicated by <c>Content-Type: text/html</c>.
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Mime"/> via the shared <c>Content-Type: </c> detection path.
        /// </remarks>
        Html = 1 << 9,

        /// <summary>
        /// Partial message (<c>Content-Type: message/partial</c> or yEnc multipart <c>=ybegin part=</c>).
        /// </summary>
        /// <remarks>
        /// yEnc multipart detection also sets <see cref="YEnc"/> and <see cref="Binary"/>.
        /// </remarks>
        Partial = 1 << 10,

        /// <summary>
        /// OpenPGP message armor detected in the body (<c>-----BEGIN PGP MESSAGE-----</c>).
        /// </summary>
        /// <remarks>
        /// Does not set <see cref="Binary"/> by itself; body-line detection only after quoted-line prefix stripping.
        /// </remarks>
        PgpMessage = 1 << 11,

        /// <summary>
        /// Moderated or approved control traffic indicated by an <c>Approved:</c> header.
        /// </summary>
        /// <remarks>
        /// Header-phase detection only. Useful for moderated groups, control messages, metrics, and future policy.
        /// </remarks>
        Approved = 1 << 12,

        /// <summary>
        /// Replace semantics indicated by a <c>Supersedes:</c> header.
        /// </summary>
        /// <remarks>
        /// Header-phase detection only. Useful for replace workflows and future cancel/supersede policy.
        /// </remarks>
        Supersedes = 1 << 13,

        /// <summary>
        /// OpenPGP detached signature armor or <c>multipart/signed</c> MIME wrapper detected.
        /// </summary>
        /// <remarks>
        /// Set from <c>Content-Type: multipart/signed</c> or body armor <c>-----BEGIN PGP SIGNATURE-----</c>.
        /// </remarks>
        PgpSigned = 1 << 14,

        /// <summary>
        /// S/MIME signed or enveloped content detected via pkcs7 MIME types or parameters.
        /// </summary>
        /// <remarks>
        /// Matches <c>application/pkcs7-mime</c>, <c>application/x-pkcs7-mime</c>, or
        /// <c>multipart/signed</c> with an <c>application/pkcs7-signature</c> protocol token.
        /// </remarks>
        Smime = 1 << 15,

        /// <summary>
        /// <c>Content-Type: multipart/mixed</c> detected.
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Mime"/> and <see cref="Multipart"/>.
        /// </remarks>
        MultipartMixed = 1 << 16,

        /// <summary>
        /// <c>Content-Type: multipart/alternative</c> detected.
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Mime"/> and <see cref="Multipart"/>.
        /// </remarks>
        MultipartAlternative = 1 << 17,

        /// <summary>
        /// <c>Content-Type: multipart/related</c> detected.
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Mime"/> and <see cref="Multipart"/>.
        /// </remarks>
        MultipartRelated = 1 << 18,

        /// <summary>
        /// <c>Content-Type: multipart/signed</c> detected as a MIME subtype.
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Mime"/>, <see cref="Multipart"/>, and typically <see cref="PgpSigned"/>.
        /// </remarks>
        MultipartSigned = 1 << 19,

        /// <summary>
        /// Archive MIME family detected (<c>application/zip</c>, RAR, 7z, and related types).
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Binary"/> and <see cref="Mime"/>.
        /// </remarks>
        Archive = 1 << 20,

        /// <summary>
        /// Image MIME family detected (<c>Content-Type: image/</c> prefix).
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Binary"/> and <see cref="Mime"/>.
        /// </remarks>
        Image = 1 << 21,

        /// <summary>
        /// Video MIME family detected (<c>Content-Type: video/</c> prefix).
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Binary"/> and <see cref="Mime"/>.
        /// </remarks>
        Video = 1 << 22,

        /// <summary>
        /// Audio MIME family detected (<c>Content-Type: audio/</c> prefix).
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Binary"/> and <see cref="Mime"/>.
        /// </remarks>
        Audio = 1 << 23,

        /// <summary>
        /// Automated NZB-driven upload indicated by poster or user-agent hints.
        /// </summary>
        /// <remarks>
        /// Matched on <c>X-Newsposter:</c> or <c>User-Agent:</c> against known automated poster tokens.
        /// </remarks>
        NzbGenerated = 1 << 24,

        /// <summary>
        /// Large crosspost detected (<c>Newsgroups:</c> lists at least ten groups).
        /// </summary>
        /// <remarks>
        /// Derived in <see cref="ArticleTypeClassifier"/> finalize from comma-separated newsgroup token counting.
        /// </remarks>
        MassCrosspost = 1 << 25,

        /// <summary>
        /// Followup redirect detected when <c>Followup-To:</c> differs from <c>Newsgroups:</c>.
        /// </summary>
        /// <remarks>
        /// Derived in finalize when both headers are present, <c>Followup-To</c> is non-empty, and normalized values differ.
        /// </remarks>
        FollowupRedirect = 1 << 26,

        /// <summary>
        /// Plain text MIME family detected (<c>Content-Type: text/plain</c>).
        /// </summary>
        /// <remarks>
        /// Also sets <see cref="Mime"/>. Does not set <see cref="Binary"/> by itself.
        /// </remarks>
        Text = 1u << 29,

        /// <summary>
        /// Signed control message: <see cref="Control"/> with <see cref="Approved"/> and cryptographic signature markers.
        /// </summary>
        /// <remarks>
        /// Derived in finalize when <see cref="Control"/> and <see cref="Approved"/> are set together with
        /// <see cref="PgpSigned"/> or <see cref="Smime"/>. Metadata only — no spool rejection today.
        /// </remarks>
        SignedControl = 1u << 30,
    }
}
