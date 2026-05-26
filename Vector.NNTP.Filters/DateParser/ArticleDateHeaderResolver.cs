// <copyright file="ArticleDateHeaderResolver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// ArticleDateHeaderResolver.cs -- Scans ordered candidate header names and returns the first canonical date from NewsDateParser.
//
// Thread safety:
//   All members are static and stateless.

namespace Vector.NNTP.Filters.DateParser
{
    /// <summary>
    /// Chooses a date-like header from an article header list and returns a canonical RFC 5322-style UTC value.
    /// </summary>
    /// <remarks>
    /// <para><b>Selection:</b> Candidate names are tried in order: <c>Date</c>, then common alternates used by injectors
    /// and servers. The first header whose name matches (case-insensitive) and whose value parses wins.</para>
    ///
    /// <para><b>Thread safety:</b> Stateless static helper.</para>
    ///
    /// <para><b>Performance:</b> HOT PATH — iterates the provided header list; success path allocates only the returned
    /// canonical date string.</para>
    /// </remarks>
    public static class ArticleDateHeaderResolver
    {
        /// <summary>
        /// Header field names probed in order (case-insensitive match against the supplied list).
        /// </summary>
        private static readonly string[] CandidateHeaderNames =
        [
            "Date",
            "Injection-Date",
            "NNTP-Posting-Date",
            "Posted",
            "X-Date",
            "Delivery-Date",
        ];

        /// <summary>
        /// Tries each candidate header name in order; returns the first successfully canonicalized value.
        /// </summary>
        /// <param name="headers">Header name/value pairs in document order (for example as read from an article).</param>
        /// <param name="canonicalValue">When this method returns <see langword="true"/>, the canonical <c>ddd, dd MMM yyyy HH:mm:ss +0000</c> string.</param>
        /// <param name="failure">When this method returns <see langword="false"/>, the parse failure reason.</param>
        /// <returns><see langword="true"/> when any candidate produced a canonical date.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers"/> is <see langword="null"/>.</exception>
        public static bool TryGetCanonicalArticleDate(
            IReadOnlyList<(string Name, string Value)> headers,
            out string canonicalValue,
            out DateParseFailureReason failure) =>
            TryGetCanonicalArticleDate(headers, DateParseOptions.Default, out canonicalValue, out failure);

        /// <summary>
        /// Tries each candidate header name in order using the supplied <see cref="DateParseOptions"/>; returns the first canonical date.
        /// </summary>
        /// <param name="headers">Header name/value pairs in document order.</param>
        /// <param name="options">Guards and normalization passed to <see cref="NewsDateParser.TryGetCanonicalDateValue(ReadOnlySpan{char}, DateParseOptions, out string, out DateParseFailureReason)"/>.</param>
        /// <param name="canonicalValue">When this method returns <see langword="true"/>, the canonical date string.</param>
        /// <param name="failure">When this method returns <see langword="false"/>, the parse failure reason.</param>
        /// <returns><see langword="true"/> when any candidate produced a canonical date.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers"/> is <see langword="null"/>.</exception>
        public static bool TryGetCanonicalArticleDate(
            IReadOnlyList<(string Name, string Value)> headers,
            DateParseOptions options,
            out string canonicalValue,
            out DateParseFailureReason failure)
        {
            ArgumentNullException.ThrowIfNull(headers);

            canonicalValue = string.Empty;
            failure = DateParseFailureReason.Empty;

            if (headers.Count == 0)
            {
                return false;
            }

            foreach (string candidate in CandidateHeaderNames)
            {
                foreach ((string name, string value) in headers)
                {
                    if (!string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (NewsDateParser.TryGetCanonicalDateValue(value, options, out canonicalValue, out failure))
                    {
                        return true;
                    }
                }
            }

            failure = DateParseFailureReason.ParseFailed;
            return false;
        }
    }
}

