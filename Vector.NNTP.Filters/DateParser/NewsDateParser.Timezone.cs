// <copyright file="NewsDateParser.Timezone.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// NewsDateParser.Timezone.cs -- Trailing-letter detection and timezone abbreviation substitution for NewsDateParser.
//
// Thread safety:
//   Stateless helpers; uses the frozen TimezoneMappings built at type initialization.

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Timezone abbreviation substitution partial for <see cref="NewsDateParser"/>.
    /// </summary>
    /// <remarks>
    /// <para>Trailing <c> AEST</c>-style tokens are matched with a cached regex and replaced using the frozen
    /// <see cref="TimezoneMappings"/> table before exact parsing runs.</para>
    /// </remarks>
    public static partial class NewsDateParser
    {
        /// <summary>
        /// Returns <see langword="true"/> when the last UTF-16 unit of <paramref name="input"/> is an ASCII letter.
        /// </summary>
        /// <param name="input">Non-empty string whose final character is inspected.</param>
        /// <returns><see langword="true"/> when the last code unit is <c>A</c>–<c>Z</c> or <c>a</c>–<c>z</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EndsWithAsciiLetter(string input)
        {
            char c = input[^1];
            return (uint)((c | 0x20) - 'a') <= 'z' - 'a';
        }

        /// <summary>
        /// Replaces a trailing <c> AEST</c>-style abbreviation with a numeric offset from <see cref="TimezoneMappings"/> when known.
        /// </summary>
        /// <param name="input">The cleaned date string (may already end with a numeric zone).</param>
        /// <returns>The original string when no substitution applies; otherwise a new string with the suffix replaced.</returns>
        private static string SubstituteTimezoneAbbreviation(string input)
        {
            if (input.Length == 0 || !EndsWithAsciiLetter(input))
            {
                return input;
            }

            Match match = CachedTimezoneAbbrRegex.Match(input);
            if (!match.Success)
            {
                return input;
            }

            string abbr = match.Groups[1].Value;
            if (!TimezoneMappings.TryGetValue(abbr, out string? offset))
            {
                return input;
            }

            return string.Concat(input.AsSpan(0, match.Index), offset.AsSpan());
        }
    }
}

