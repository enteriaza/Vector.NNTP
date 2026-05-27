// <copyright file="DateParseOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// DateParseOptions.cs -- Tunable guards and normalization flags for NewsDateParser; passed as a readonly struct on hot paths.
//
// Thread safety:
//   Immutable after construction; safe to share across threads.

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Optional behaviour for Usenet date parsing and canonicalization.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread safety:</b> Immutable after construction; safe to share across threads.</para>
    ///
    /// <para><b>Performance:</b> HOT PATH — passed by value; success paths avoid allocations other than the returned
    /// canonical string.</para>
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="DateParseOptions"/> struct.
    /// </remarks>
    /// <param name="maxInputLength">Maximum number of characters accepted for a single date header value.</param>
    /// <param name="requireKnownTimezoneAbbreviation">When <see langword="true"/>, trailing abbreviation patterns must map to the frozen table.</param>
    /// <param name="normalizeInteriorWhitespace">When <see langword="true"/>, runs of ASCII spaces are collapsed to a single space before parsing.</param>
    public readonly struct DateParseOptions(int maxInputLength = 512, bool requireKnownTimezoneAbbreviation = false, bool normalizeInteriorWhitespace = true)
    {

        /// <summary>
        /// Default options for overloads that omit an explicit value: 512-character cap, unknown abbreviations allowed,
        /// interior whitespace normalized.
        /// </summary>
        public static DateParseOptions Default => new(maxInputLength: 512, requireKnownTimezoneAbbreviation: false, normalizeInteriorWhitespace: true);

        /// <summary>Maximum characters accepted for one header value before <see cref="DateParseFailureReason.TooLong"/> is returned.</summary>
        public int MaxInputLength { get; } = maxInputLength;

        /// <summary>
        /// When <see langword="true"/>, a trailing abbreviation token that does not map in <see cref="NewsDateParser"/> timezone table
        /// yields <see cref="DateParseFailureReason.UnknownTimezoneAbbreviation"/>.
        /// </summary>
        public bool RequireKnownTimezoneAbbreviation { get; } = requireKnownTimezoneAbbreviation;

        /// <summary>
        /// When <see langword="true"/>, adjacent ASCII spaces are collapsed to a single U+0020 after trim before parsing.
        /// </summary>
        public bool NormalizeInteriorWhitespace { get; } = normalizeInteriorWhitespace;
    }
}

