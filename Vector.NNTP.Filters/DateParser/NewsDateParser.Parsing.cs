// <copyright file="NewsDateParser.Parsing.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// NewsDateParser.Parsing.cs -- Span/string parsing pipeline: normalization, quick parse, exact parse, and canonical value helpers.
//
// Thread safety:
//   All members are static; safe for concurrent use.

using System.Globalization;

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Parsing partial for <see cref="NewsDateParser"/> — span and string entry points, normalization, and private parse stages.
    /// </summary>
    /// <remarks>
    /// <para><b>Pipeline:</b> trim → length guard → quick invariant parse → optional interior whitespace collapse →
    /// parenthesis strip → timezone abbreviation substitution → exact format list.</para>
    /// <para><b>Performance:</b> HOT PATH — success on quick parse avoids heap allocations; string path may allocate when
    /// normalization or substitution produces a new string.</para>
    /// </remarks>
    public static partial class NewsDateParser
    {
        /// <summary>
        /// Attempts to parse a date header value to UTC using <see cref="DateParseOptions.Default"/>.
        /// </summary>
        /// <param name="dateSpan">Raw characters (typically a single header field value).</param>
        /// <param name="result">When this method returns <see langword="true"/>, the UTC <see cref="DateTime"/> (kind <see cref="DateTimeKind.Utc"/>).</param>
        /// <returns><see langword="true"/> when the value parses; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// Discards the failure reason. Use the overload with <see cref="DateParseFailureReason"/> when callers must distinguish
        /// empty, too-long, and parse failures.
        /// </remarks>
        public static bool TryParseToUtc(ReadOnlySpan<char> dateSpan, out DateTime result)
        {
            return TryParseToUtc(dateSpan, DateParseOptions.Default, out result, out _);
        }

        /// <summary>
        /// Attempts to parse a date header value to UTC, with failure classification.
        /// </summary>
        /// <param name="dateSpan">Raw characters (typically a single header field value).</param>
        /// <param name="options">Guards and normalization behaviour.</param>
        /// <param name="result">When this method returns <see langword="true"/>, the UTC <see cref="DateTime"/> (kind <see cref="DateTimeKind.Utc"/>).</param>
        /// <param name="failure">When this method returns <see langword="false"/>, the reason the value was rejected.</param>
        /// <returns><see langword="true"/> when the value parses; otherwise <see langword="false"/>.</returns>
        public static bool TryParseToUtc(ReadOnlySpan<char> dateSpan, DateParseOptions options, out DateTime result, out DateParseFailureReason failure)
        {
            result = default;
            failure = DateParseFailureReason.None;

            ReadOnlySpan<char> trimmed = dateSpan.Trim();
            if (trimmed.IsEmpty)
            {
                failure = DateParseFailureReason.Empty;
                return false;
            }

            if (trimmed.Length > options.MaxInputLength)
            {
                failure = DateParseFailureReason.TooLong;
                return false;
            }

            if (TryQuickParse(trimmed, out result))
            {
                return true;
            }

            if (options.NormalizeInteriorWhitespace && NeedsInteriorWhitespaceCollapse(trimmed))
            {
                string collapsed = CollapseInteriorWhitespace(trimmed.ToString());
                return TryParseToUtc(
                    collapsed,
                    new DateParseOptions(options.MaxInputLength, options.RequireKnownTimezoneAbbreviation, normalizeInteriorWhitespace: false),
                    out result,
                    out failure);
            }

            return TryParseToUtc(trimmed.ToString(), options, out result, out failure);
        }

        /// <summary>
        /// Attempts to parse a date header value to UTC using <see cref="DateParseOptions.Default"/>.
        /// </summary>
        /// <param name="dateRaw">The raw header value (may include comments or odd spacing).</param>
        /// <param name="result">When this method returns <see langword="true"/>, the UTC instant.</param>
        /// <returns><see langword="true"/> when the pipeline yields a UTC instant.</returns>
        /// <remarks>
        /// Delegates to <see cref="TryParseToUtc(string, DateParseOptions, out DateTime, out DateParseFailureReason)"/> with
        /// default options and discards the failure classification.
        /// </remarks>
        public static bool TryParseToUtc(string dateRaw, out DateTime result)
        {
            return TryParseToUtc(dateRaw, DateParseOptions.Default, out result, out _);
        }

        /// <summary>
        /// Full string pipeline: optional interior space collapse, parenthesis strip, quick parse, timezone substitution, and exact parse.
        /// </summary>
        /// <param name="dateRaw">The raw header value (may include comments or odd spacing).</param>
        /// <param name="options">Guards and normalization behaviour.</param>
        /// <param name="result">When this method returns <see langword="true"/>, the UTC instant.</param>
        /// <param name="failure">When this method returns <see langword="false"/>, the failure classification.</param>
        /// <returns><see langword="true"/> when the pipeline yields a UTC instant.</returns>
        public static bool TryParseToUtc(string dateRaw, DateParseOptions options, out DateTime result, out DateParseFailureReason failure)
        {
            result = default;
            failure = DateParseFailureReason.None;

            if (string.IsNullOrEmpty(dateRaw))
            {
                failure = DateParseFailureReason.Empty;
                return false;
            }

            ReadOnlySpan<char> trimmedSpan = dateRaw.AsSpan().Trim();
            if (trimmedSpan.IsEmpty)
            {
                failure = DateParseFailureReason.Empty;
                return false;
            }

            if (trimmedSpan.Length > options.MaxInputLength)
            {
                failure = DateParseFailureReason.TooLong;
                return false;
            }

            string cleaned = trimmedSpan.Length == dateRaw.Length ? dateRaw : trimmedSpan.ToString();
            if (options.NormalizeInteriorWhitespace)
            {
                cleaned = CollapseInteriorWhitespace(cleaned);
            }

            if (cleaned.Contains('('))
            {
                cleaned = CachedParenthesisedTzRegex.Replace(cleaned, string.Empty).Trim();
            }

            if (options.RequireKnownTimezoneAbbreviation && TryGetUnknownTrailingAbbreviation(cleaned, out _))
            {
                failure = DateParseFailureReason.UnknownTimezoneAbbreviation;
                return false;
            }

            if (TryQuickParse(cleaned, out result))
            {
                return true;
            }

            string substituted = SubstituteTimezoneAbbreviation(cleaned);
            bool wasSubstituted = !ReferenceEquals(substituted, cleaned);

            if (wasSubstituted && TryQuickParse(substituted, out result))
            {
                return true;
            }

            if (wasSubstituted && TryExactParse(substituted, out result))
            {
                return true;
            }

            if (TryExactParse(cleaned, out result))
            {
                return true;
            }

            failure = !PrintableAsciiSimd.IsAllPrintableAscii(cleaned.AsSpan())
                ? DateParseFailureReason.NonPrintableAscii
                : DateParseFailureReason.ParseFailed;
            return false;
        }

        /// <summary>
        /// Parses to Unix seconds using <see cref="DateParseOptions.Default"/>, or <c>0</c> on failure or overflow.
        /// </summary>
        /// <param name="dateSpan">Raw characters to interpret as a date.</param>
        /// <returns>Seconds since 1970-01-01Z, or <c>0</c> when parsing fails or the instant is outside the <see cref="uint"/> range.</returns>
        public static uint ParseToUnixTimestamp(ReadOnlySpan<char> dateSpan)
        {
            return ParseToUnixTimestamp(dateSpan, DateParseOptions.Default);
        }

        /// <summary>
        /// Parses to Unix seconds, or <c>0</c> when parsing fails or the instant is outside the <see cref="uint"/> epoch range.
        /// </summary>
        /// <param name="dateSpan">Raw characters to interpret as a date.</param>
        /// <param name="options">Guards and normalization behaviour.</param>
        /// <returns>Seconds since 1970-01-01Z, or <c>0</c> on failure or overflow.</returns>
        public static uint ParseToUnixTimestamp(ReadOnlySpan<char> dateSpan, DateParseOptions options)
        {
            ReadOnlySpan<char> trimmed = dateSpan.Trim();
            return trimmed.IsEmpty ? 0 : TryParseToUtc(trimmed, options, out DateTime utc, out _) ? UsenetEpoch.ToUnixTimestamp(utc) : 0;
        }

        /// <summary>
        /// Parses to Unix seconds using <see cref="DateParseOptions.Default"/>, or <c>0</c> on failure or overflow.
        /// </summary>
        /// <param name="dateRaw">The raw date string.</param>
        /// <returns>Seconds since 1970-01-01Z, or <c>0</c> when parsing fails or the instant is outside the <see cref="uint"/> range.</returns>
        public static uint ParseToUnixTimestamp(string dateRaw)
        {
            return ParseToUnixTimestamp(dateRaw, DateParseOptions.Default);
        }

        /// <summary>
        /// Parses a string date value to Unix seconds, or <c>0</c> on failure or out-of-range.
        /// </summary>
        /// <param name="dateRaw">The raw date string.</param>
        /// <param name="options">Guards and normalization behaviour.</param>
        /// <returns>Seconds since 1970-01-01Z, or <c>0</c> on failure or overflow.</returns>
        public static uint ParseToUnixTimestamp(string dateRaw, DateParseOptions options)
        {
            return TryParseToUtc(dateRaw, options, out DateTime utc, out _) ? UsenetEpoch.ToUnixTimestamp(utc) : 0;
        }

        /// <summary>
        /// Attempts to parse to a UTC <see cref="DateTimeOffset"/> (offset always zero).
        /// </summary>
        /// <param name="dateSpan">Raw characters to interpret as a date.</param>
        /// <param name="options">Guards and normalization behaviour.</param>
        /// <param name="dto">When this method returns <see langword="true"/>, a UTC offset with zero offset.</param>
        /// <param name="failure">When this method returns <see langword="false"/>, the failure classification.</param>
        /// <returns><see langword="true"/> when parsing succeeds.</returns>
        public static bool TryParseToDateTimeOffset(ReadOnlySpan<char> dateSpan, DateParseOptions options, out DateTimeOffset dto, out DateParseFailureReason failure)
        {
            dto = default;
            if (!TryParseToUtc(dateSpan, options, out DateTime utc, out failure))
            {
                return false;
            }

            dto = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero);
            return true;
        }

        /// <summary>
        /// Parses and returns the canonical RFC 5322-style UTC header value using <see cref="DateParseOptions.Default"/>.
        /// </summary>
        /// <param name="raw">The raw date characters.</param>
        /// <param name="canonicalValue">When this method returns <see langword="true"/>, the canonical string.</param>
        /// <returns><see langword="true"/> when parsing succeeds.</returns>
        /// <remarks>
        /// Discards the failure reason. Use the overload with <see cref="DateParseFailureReason"/> when callers must report why
        /// canonicalization failed.
        /// </remarks>
        public static bool TryGetCanonicalDateValue(ReadOnlySpan<char> raw, out string canonicalValue)
        {
            return TryGetCanonicalDateValue(raw, DateParseOptions.Default, out canonicalValue, out _);
        }

        /// <summary>
        /// Parses and returns the canonical RFC 5322-style UTC header value (<c>... +0000</c>), with failure classification.
        /// </summary>
        /// <param name="raw">The raw date characters.</param>
        /// <param name="options">Guards and normalization behaviour.</param>
        /// <param name="canonicalValue">When this method returns <see langword="true"/>, the canonical string.</param>
        /// <param name="failure">When this method returns <see langword="false"/>, the failure classification.</param>
        /// <returns><see langword="true"/> when parsing succeeds.</returns>
        public static bool TryGetCanonicalDateValue(ReadOnlySpan<char> raw, DateParseOptions options, out string canonicalValue, out DateParseFailureReason failure)
        {
            canonicalValue = string.Empty;
            if (!TryParseToUtc(raw, options, out DateTime utc, out failure))
            {
                return false;
            }

            canonicalValue = FormatCanonicalRfc5322Utc(utc);
            return true;
        }

        /// <summary>
        /// Parses a string date value and returns the canonical RFC 5322-style UTC header value.
        /// </summary>
        /// <param name="raw">The raw date string.</param>
        /// <param name="options">Guards and normalization behaviour.</param>
        /// <param name="canonicalValue">When this method returns <see langword="true"/>, the canonical string.</param>
        /// <param name="failure">When this method returns <see langword="false"/>, the failure classification.</param>
        /// <returns><see langword="true"/> when parsing succeeds.</returns>
        /// <remarks>
        /// Forwards to the span overload without allocating when <paramref name="raw"/> is already available as characters.
        /// </remarks>
        public static bool TryGetCanonicalDateValue(string raw, DateParseOptions options, out string canonicalValue, out DateParseFailureReason failure)
        {
            return TryGetCanonicalDateValue(raw.AsSpan(), options, out canonicalValue, out failure);
        }

        /// <summary>
        /// Fast path: invariant <see cref="DateTimeOffset.TryParse(ReadOnlySpan{char}, IFormatProvider?, DateTimeStyles, out DateTimeOffset)"/> after a printable-ASCII SIMD pre-check.
        /// </summary>
        /// <param name="input">Trimmed or cleaned input text.</param>
        /// <param name="result">When this method returns <see langword="true"/>, the UTC <see cref="DateTime"/>.</param>
        /// <returns><see langword="true"/> when the relaxed parse succeeds.</returns>
        private static bool TryQuickParse(ReadOnlySpan<char> input, out DateTime result)
        {
            if (!PrintableAsciiSimd.IsAllPrintableAscii(input))
            {
                result = default;
                return false;
            }

            if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, ParseStyles, out DateTimeOffset dto))
            {
                result = dto.UtcDateTime;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Tries the curated <see cref="DateFormats"/> list with invariant culture parsing.
        /// </summary>
        /// <param name="input">The string to parse (already normalized where applicable).</param>
        /// <param name="result">When this method returns <see langword="true"/>, the UTC instant.</param>
        /// <returns><see langword="true"/> when one of the exact formats matches.</returns>
        private static bool TryExactParse(string input, out DateTime result)
        {
            if (DateTimeOffset.TryParseExact(input, DateFormats, CultureInfo.InvariantCulture, ParseStyles, out DateTimeOffset dto))
            {
                result = dto.UtcDateTime;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Detects a trailing abbreviation token that is not present in <see cref="TimezoneMappings"/> (for strict mode).
        /// </summary>
        /// <param name="cleaned">Trimmed and partially normalized header value.</param>
        /// <param name="abbreviation">When this method returns <see langword="true"/>, the unknown abbreviation text.</param>
        /// <returns><see langword="true"/> when a trailing abbreviation pattern matched but is unknown.</returns>
        private static bool TryGetUnknownTrailingAbbreviation(string cleaned, out string abbreviation)
        {
            abbreviation = string.Empty;
            if (cleaned.Length == 0 || !EndsWithAsciiLetter(cleaned))
            {
                return false;
            }

            Match match = CachedTimezoneAbbrRegex.Match(cleaned);
            if (!match.Success)
            {
                return false;
            }

            string abbr = match.Groups[1].Value;
            if (TimezoneMappings.ContainsKey(abbr))
            {
                return false;
            }

            abbreviation = abbr;
            return true;
        }

        /// <summary>
        /// Returns <see langword="true"/> when the span contains at least one pair of adjacent ASCII spaces.
        /// </summary>
        /// <param name="s">Text after trim.</param>
        /// <returns><see langword="true"/> when interior collapse may change the string.</returns>
        private static bool NeedsInteriorWhitespaceCollapse(ReadOnlySpan<char> s)
        {
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] == ' ' && s[i - 1] == ' ')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Collapses runs of U+0020 to a single space (used when <see cref="DateParseOptions.NormalizeInteriorWhitespace"/> is enabled).
        /// </summary>
        /// <param name="s">The string to normalize.</param>
        /// <returns>The original string when no runs exist; otherwise a new string with collapsed spaces.</returns>
        private static string CollapseInteriorWhitespace(string s)
        {
            if (!NeedsInteriorWhitespaceCollapse(s.AsSpan()))
            {
                return s;
            }

            StringBuilder sb = new(s.Length);
            bool lastWasSpace = false;
            foreach (char c in s)
            {
                if (c == ' ')
                {
                    if (!lastWasSpace)
                    {
                        _ = sb.Append(c);
                        lastWasSpace = true;
                    }
                }
                else
                {
                    _ = sb.Append(c);
                    lastWasSpace = false;
                }
            }

            return sb.ToString();
        }
    }
}

