// <copyright file="PostFilterRejectionMessages.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterRejectionMessages.cs -- Default client-visible strings for numeric rejection codes.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Default client-visible strings for numeric codes (subset of Aioe postfilter <c>@quickref</c>; full table lives in Perl <c>rules.conf</c>).
    /// </summary>
    /// <remarks>
    /// <para>When <see cref="PostFilterOptions.ShowErrorCode"/> is <see langword="true"/>, the host typically prefixes the numeric
    /// code to the message returned to the client.</para>
    /// </remarks>
    public static class PostFilterRejectionMessages
    {
        /// <summary>
        /// Returns a default message for <paramref name="code"/>.
        /// </summary>
        /// <param name="code">Numeric rejection code.</param>
        /// <returns>Human-readable text.</returns>
        public static string GetMessage(int code)
        {
            return code switch
            {
                0 => "Message accepted",
                1 => "Syntax error in article",
                2 => "Invalid or missing Newsgroups header",
                3 => "Cross-post limit exceeded",
                4 => "Forbidden header present",
                5 => "Listed in DNS blocklist",
                6 => "URI domain listed in blocklist",
                7 => "Tor exit node not permitted",
                8 => "Banlist match",
                9 => "Bad word filter match",
                10 => "Posting rate limit exceeded",
                11 => "Custom filter rejected the message",
                12 => "Article too large",
                48 => "Server closed to posting",
                _ => $"Message rejected (code {code})",
            };
        }
    }
}

