// <copyright file="YEncSabctoolsFixtureCatalog.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// YEncSabctoolsFixtureCatalog.cs -- Curated validate/reject expectations for sabctools yEnc fixture files.

namespace Vector.NNTP.Tests.Filters.YEnc
{
    /// <summary>
    /// Curated mapping from sabctools <c>tests/yencfiles/*.yenc</c> to CRC validation expectations.
    /// </summary>
    /// <remarks>
    /// <para><b>Scope:</b> <see cref="Vector.NNTP.Filters.YEnc.YEncSectionCrc.Validate"/> is a structural/CRC gate on article
    /// bodies, not a full NNTP decoder. Fixtures that sabctools uses only to prove “no crash” may still fail CRC validation here.</para>
    ///
    /// <para><b>Source:</b> <see href="https://github.com/sabnzbd/sabctools/tree/master/tests/yencfiles">sabctools yencfiles</see>
    /// and <c>tests/test_decoder.py</c>.</para>
    ///
    /// <para><b>Product note:</b> <c>test_bad_crc.yenc</c> is expected to pass — sabctools decodes despite CRC mismatch
    /// (<c>crc_correct is None</c>); this validator does not fail closed on checksum mismatch alone when structure parses.</para>
    /// </remarks>
    public static class YEncSabctoolsFixtureCatalog
    {
        /// <summary>Relative path from test output directory to fixture files.</summary>
        public const string FixtureDirectoryRelative = "TestData/YEnc/sabctools";

        /// <summary>
        /// Every imported <c>.yenc</c> fixture and whether <see cref="Vector.NNTP.Filters.YEnc.YEncSectionCrc.Validate"/> should accept it.
        /// </summary>
        public static readonly IReadOnlyList<YEncSabctoolsFixtureExpectation> All = BuildAll();

        /// <summary>File names indexed for quick lookup.</summary>
        public static readonly IReadOnlyDictionary<string, YEncSabctoolsFixtureExpectation> ByFileName =
            All.ToDictionary(static e => e.FileName, StringComparer.Ordinal);

        /// <summary>Builds the curated fixture table (one entry per file under <see cref="FixtureDirectoryRelative"/>).</summary>
        /// <returns>Ordered list of expectations.</returns>
        private static IReadOnlyList<YEncSabctoolsFixtureExpectation> BuildAll() =>
        [
            new("capabilities.yenc", ExpectedValid: true, "NNTP CAPABILITIES text; no =ybegin."),
            new("test_head.yenc", ExpectedValid: true, "NNTP HEAD response; no yEnc payload."),
            new("test_regular.yenc", ExpectedValid: true, "sabctools: python_yenc == sabctools_yenc."),
            new("test_regular_2.yenc", ExpectedValid: true, "sabctools: python_yenc == sabctools_yenc."),
            new("test_padded_crc.yenc", ExpectedValid: true, "sabctools: padded hex CRC still decodes."),
            new("test_special_chars.yenc", ExpectedValid: true, "sabctools: decodes; non-ASCII filename only."),
            new("test_special_utf8_chars.yenc", ExpectedValid: true, "sabctools: decodes; UTF-8 filename."),
            new("test_no_name.yenc", ExpectedValid: true, "sabctools: filename None; CRC still valid."),
            new("test_article.yenc", ExpectedValid: true, "Full 220 article; multipart section CRC matches payload."),
            new("test_bad_crc.yenc", ExpectedValid: true, "sabctools decodes; crc_correct None — validator does not reject mismatch-only."),
            new("test_empty_file.yenc", ExpectedValid: true, "Empty file; no sections."),
            new("test_only_newlines.yenc", ExpectedValid: true, "Newlines only; no =ybegin."),
            new("test_only_dots.yenc", ExpectedValid: true, "Dot-stuffing edge; no yEnc section."),
            new("test_truncated_status.yenc", ExpectedValid: true, "Truncated NNTP status; no complete yEnc section."),
            new("test_partial.yenc", ExpectedValid: false, "Multipart part without valid =yend CRC metadata."),
            new("test_bad_crc_end.yenc", ExpectedValid: false, "sabctools: crc is None on =yend."),
            new("test_missing_yend.yenc", ExpectedValid: false, "=ybegin without =yend."),
            new("test_invalid_escape.yenc", ExpectedValid: false, "Truncated = escape at line end."),
            new("test_invalid_crc_chars.yenc", ExpectedValid: false, "Non-hex CRC field — fail closed."),
            new("test_extremely_long_crc.yenc", ExpectedValid: false, "CRC token present but not parseable."),
            new("test_double_ybegin.yenc", ExpectedValid: false, "Second =ybegin before =yend for first section."),
            new("test_end_after_filename.yenc", ExpectedValid: false, "sabctools: decoded_data is None."),
            new("test_end_after_ypart.yenc", ExpectedValid: false, "sabctools: decoded_data is None."),
            new("test_malformed_ybegin.yenc", ExpectedValid: true, "=ybegin/=yend with empty payload; CRC 00000000 matches zero decoded bytes."),
            new("test_negative_size.yenc", ExpectedValid: false, "Negative declared size."),
            new("test_huge_size.yenc", ExpectedValid: false, "Declared size overflows practical decode."),
            new("test_huge_size_1TiB.yenc", ExpectedValid: false, "Declared 1 TiB size mismatch."),
            new("test_huge_size_1TiB_ypart.yenc", ExpectedValid: false, "Declared size does not match decoded length."),
            new("test_ypart_without_ybegin.yenc", ExpectedValid: true, "No =ybegin line; validator finds no sections (vacuous pass)."),
            new("test_ypart_invalid_range.yenc", ExpectedValid: false, "Part begin &gt; end in =ypart."),
            new("test_part_exceeds_limit.yenc", ExpectedValid: false, "Part size metadata inconsistent."),
            new("test_ypart_greater_size.yenc", ExpectedValid: false, "Declared size=9 vs ybegin size=1000."),
            new("test_non_ascii_everywhere.yenc", ExpectedValid: false, "Declared CRC/size do not match payload."),
            new("test_null_bytes_filename.yenc", ExpectedValid: false, "Filename/control bytes; CRC does not match payload."),
            new("test_invalid_status_code.yenc", ExpectedValid: false, "Malformed wrapper; yEnc metadata does not validate."),
        ];
    }
}
