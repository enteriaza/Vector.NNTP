// <copyright file="DateParseFailureReason.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// DateParseFailureReason.cs -- Classification values returned from NewsDateParser when a date string cannot be parsed or normalized.
//
// Thread safety:
//   Enum values are immutable.

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Describes why a Usenet date value could not be parsed or normalized.
    /// </summary>
    /// <remarks>
    /// <para><b>Consumers:</b> Inspect <see cref="NewsDateParser"/> parse overloads; <see cref="None"/> is only meaningful
    /// when the parse API returns <see langword="true"/>.</para>
    /// </remarks>
    public enum DateParseFailureReason
    {
        /// <summary>Used when the corresponding <see cref="NewsDateParser"/> parse operation succeeds.</summary>
        None = 0,

        /// <summary>The input was empty or whitespace only.</summary>
        Empty = 1,

        /// <summary>The input exceeded <see cref="DateParseOptions.MaxInputLength"/>.</summary>
        TooLong = 2,

        /// <summary>The SIMD printable-ASCII pre-check failed (control or non-ASCII).</summary>
        NonPrintableAscii = 3,

        /// <summary>Reserved for fixed-buffer normalization paths; not currently emitted by this library.</summary>
        NormalizationBufferTooSmall = 4,

        /// <summary>
        /// <see cref="DateParseOptions.RequireKnownTimezoneAbbreviation"/> was set and the value ended with a letter
        /// sequence matched as an abbreviation but was not present in the timezone table.
        /// </summary>
        UnknownTimezoneAbbreviation = 5,

        /// <summary>No parse stage accepted the value (including after timezone substitution and exact formats).</summary>
        ParseFailed = 6,
    }
}

