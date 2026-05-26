// <copyright file="NewsDateParser.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// NewsDateParser.cs -- Core partial: format strings, cached regex sources, timezone map hook, and canonical UTC formatting.
//
// Thread safety:
//   All members are static and read-only after type initialization; safe for concurrent use.

using System.Collections.Frozen;
using System.Globalization;

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Parses Usenet <c>Date:</c> header values and produces a canonical RFC 5322-style UTC string (<c>+0000</c> offset).
    /// </summary>
    /// <remarks>
    /// <para><b>Thread safety:</b> All members are static and read-only after type initialization; safe for concurrent use.</para>
    ///
    /// <para><b>Performance:</b> HOT PATH — a printable-ASCII SIMD pre-check rejects garbage before parsing; curated exact
    /// formats handle common legacy forms. Allocation occurs only when returning canonical strings or normalizing whitespace.</para>
    /// </remarks>
    public static partial class NewsDateParser
    {
        /// <summary>
        /// Parsing flags passed to invariant <see cref="DateTimeOffset"/> parsing on quick and exact paths.
        /// </summary>
        private const DateTimeStyles ParseStyles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal;

        /// <summary>
        /// Frozen map from trailing timezone abbreviation (case-insensitive) to ISO-8601 numeric offset string (for example <c>+10:00</c>).
        /// </summary>
        /// <remarks>Populated by <see cref="CreateDefaultTimezoneMappings"/> in <c>NewsDateParser.DefaultTimezones.cs</c>.</remarks>
        private static readonly FrozenDictionary<string, string> TimezoneMappings = CreateDefaultTimezoneMappings();

        /// <summary>
        /// Exact format patterns supplied to <see cref="DateTimeOffset.TryParseExact(ReadOnlySpan{char}, string[], IFormatProvider?, DateTimeStyles, out DateTimeOffset)"/> after normalization.
        /// </summary>
        private static readonly string[] DateFormats =
        [
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, d MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy H:mm:ss zzz",
            "ddd, d MMM yyyy H:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm:ss",
            "ddd, d MMM yyyy HH:mm:ss",
            "ddd, dd MMM yyyy H:mm:ss",
            "ddd, d MMM yyyy H:mm:ss",
            "dd MMM yyyy HH:mm:ss zzz",
            "d MMM yyyy HH:mm:ss zzz",
            "dd MMM yyyy H:mm:ss zzz",
            "d MMM yyyy H:mm:ss zzz",
            "dd MMM yyyy HH:mm:ss",
            "d MMM yyyy HH:mm:ss",
            "dd MMM yyyy H:mm:ss",
            "d MMM yyyy H:mm:ss",
            "yyyy-MM-dd HH:mm:ss zzz",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd H:mm:ss",
            "yyyy-MM-ddTHH:mm:ss zzz",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd H:mm:ss",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy H:mm:ss",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy H:mm:ss",
            "dd MMM yyyy",
            "d MMM yyyy",
            "yyyy-MM-dd",
            "dd/MM/yyyy",
            "MM/dd/yyyy",
            "ddd, dd MMM yy HH:mm:ss zzz",
            "ddd, d MMM yy HH:mm:ss zzz",
            "ddd, dd MMM yy H:mm:ss zzz",
            "ddd, d MMM yy H:mm:ss zzz",
            "ddd, dd MMM yy HH:mm:ss",
            "ddd, d MMM yy HH:mm:ss",
            "ddd, dd MMM yy H:mm:ss",
            "ddd, d MMM yy H:mm:ss",
            "dd MMM yy HH:mm:ss zzz",
            "d MMM yy HH:mm:ss zzz",
            "dd MMM yy H:mm:ss zzz",
            "d MMM yy H:mm:ss zzz",
            "dd MMM yy HH:mm:ss",
            "d MMM yy HH:mm:ss",
            "dd MMM yy H:mm:ss",
            "d MMM yy H:mm:ss",
        ];

        /// <summary>Cached regex matching a trailing <c> AEST</c>-style abbreviation (two to five ASCII letters).</summary>
        private static readonly Regex CachedTimezoneAbbrRegex = TimezoneAbbrRegex();

        /// <summary>Cached regex removing a parenthetical comment at the end of a date string (for example <c>(UTC)</c>).</summary>
        private static readonly Regex CachedParenthesisedTzRegex = ParenthesisedTzRegex();

        /// <summary>Compiles the trailing timezone abbreviation pattern.</summary>
        /// <returns>A culture-invariant <see cref="Regex"/>.</returns>
        [GeneratedRegex(@"\s([A-Za-z]{2,5})$", RegexOptions.CultureInvariant)]
        private static partial Regex TimezoneAbbrRegex();

        /// <summary>Compiles the parenthetical suffix strip pattern.</summary>
        /// <returns>A culture-invariant <see cref="Regex"/>.</returns>
        [GeneratedRegex(@"\s*\([^)]*\)\s*$", RegexOptions.CultureInvariant)]
        private static partial Regex ParenthesisedTzRegex();

        /// <summary>
        /// Formats a UTC instant as <c>ddd, dd MMM yyyy HH:mm:ss +0000</c> (RFC 5322-style numeric zone, always UTC).
        /// </summary>
        /// <param name="utc">The instant to format; local or unspecified kinds are normalized to UTC first.</param>
        /// <returns>The formatted date string with a literal <c> +0000</c> suffix.</returns>
        public static string FormatCanonicalRfc5322Utc(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Local)
            {
                utc = utc.ToUniversalTime();
            }
            else if (utc.Kind == DateTimeKind.Unspecified)
            {
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            }

            return utc.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " +0000";
        }
    }
}

