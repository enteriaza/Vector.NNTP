// <copyright file="PathHeaderFeedResolver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Path header first-hop extraction for INN news log feed resolution.

using System.Text;
using Vector.NNTP.Articles.Classification;
using Vector.NNTP.Articles.Scanning;

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Extracts a candidate incoming feed name from the first usable hop of a <c>Path</c> header for INN-style news logging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consumed by <see cref="NntpNewsFeedResolver"/> after local-post and transit-peer-name checks fail. The resolved token
    /// becomes the feed field on accept, reject, cancel, and future junk lines written by <see cref="INntpNewsLog"/>.
    /// </para>
    /// <para><b>Extraction rules:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// Scan physical header lines in order (from the start of the article through the header-body blank line).
    /// Continuation folding is not applied; each line is evaluated independently.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Match a line whose field name is <c>Path</c> using ASCII case-insensitive comparison via
    /// <see cref="HeaderValueUtilities.TryGetHeaderValue"/> and <see cref="PathPrefix"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Take the substring before the first exclamation mark in the header value (or the entire value when no bang is present).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Trim ASCII space and tab from both ends of that hop. Reject empty hops and hops equal to
    /// <see cref="NntpNewsLogFeedNames.NotForMail"/> (case-insensitive). When a <c>Path</c> line matches but the hop is
    /// rejected, scanning continues so a later <c>Path</c> line may still supply a feed.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Encoding:</b> Hop text is decoded with <see cref="Encoding.ASCII"/> for logging. Path values are expected to be
    /// ASCII host or site tokens as in typical NNTP transit articles.
    /// </para>
    /// <para><b>Thread safety:</b> All members are static and stateless; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal static class PathHeaderFeedResolver
    {
        /// <summary>
        /// Literal prefix bytes for a canonical <c>Path</c> header line, including the trailing space after the colon.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Passed to <see cref="HeaderValueUtilities.TryGetHeaderValue"/>, which performs ASCII case-insensitive prefix
        /// matching. A line beginning with <c>path: news.example.com</c> therefore matches even though this constant uses
        /// mixed case.
        /// </para>
        /// </remarks>
        private static ReadOnlySpan<byte> PathPrefix => "Path: "u8;

        /// <summary>
        /// Attempts to resolve a feed token from the first usable <c>Path</c> header in an article.
        /// </summary>
        /// <param name="articleBytes">
        /// Raw article bytes including headers and the header-body separator. May be empty when callers have no payload
        /// bytes for Path lookup.
        /// </param>
        /// <param name="feed">
        /// When this method returns <see langword="true"/>, the first-hop feed token extracted from a <c>Path</c> header
        /// (ASCII, without bang-separated downstream hops). When this method returns <see langword="false"/>, set to
        /// <see cref="string.Empty"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a non-empty, non-<see cref="NntpNewsLogFeedNames.NotForMail"/> first hop was found;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para><b>Failure paths (all return <see langword="false"/> and leave <paramref name="feed"/> empty):</b></para>
        /// <list type="bullet">
        /// <item><description><paramref name="articleBytes"/> is empty.</description></item>
        /// <item>
        /// <description>
        /// <see cref="ArticleByteScanSimd.FindHeaderEnd"/> cannot locate a header-body terminator (malformed article).
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// No physical header line both matches <c>Path</c> and yields a usable first hop via
        /// <see cref="TryExtractFirstHop"/>.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Does not throw for malformed input; unexpected exceptions would indicate an implementation defect rather than a
        /// normal reject path.
        /// </para>
        /// </remarks>
        /// <example>
        /// For header line <c>Path: news.example.com!not-for-mail</c>, this method returns feed
        /// <c>news.example.com</c> because only the hop before the bang is considered and downstream hops are ignored.
        /// </example>
        internal static bool TryResolveFeed(ReadOnlySpan<byte> articleBytes, out string feed)
        {
            feed = string.Empty;
            if (articleBytes.IsEmpty)
            {
                return false;
            }

            int headerEnd = ArticleByteScanSimd.FindHeaderEnd(articleBytes);
            if (headerEnd < 0)
            {
                return false;
            }

            int index = 0;
            while (index < headerEnd)
            {
                int lineEnd = ArticleByteScanSimd.IndexOfLineFeed(articleBytes, index, headerEnd);
                int contentEnd = lineEnd;
                if (contentEnd > index && articleBytes[contentEnd - 1] == '\r')
                {
                    contentEnd--;
                }

                ReadOnlySpan<byte> line = articleBytes[index..contentEnd];
                if (HeaderValueUtilities.TryGetHeaderValue(line, PathPrefix, out ReadOnlySpan<byte> pathValue) &&
                    TryExtractFirstHop(pathValue, out string? candidate) &&
                    candidate is not null)
                {
                    feed = candidate;
                    return true;
                }

                index = lineEnd + 1;
            }

            return false;
        }

        /// <summary>
        /// Parses the first path hop from a <c>Path</c> header value already isolated from the field name and colon.
        /// </summary>
        /// <param name="pathValue">
        /// Header value bytes after the <c>Path</c> field prefix (typically already trimmed by
        /// <see cref="HeaderValueUtilities.TryGetHeaderValue"/>). Must not include line terminators.
        /// </param>
        /// <param name="firstHop">
        /// When this method returns <see langword="true"/>, the first hop text before the first bang, with ASCII
        /// space and tab trimmed from both ends. When this method returns <see langword="false"/>, set to
        /// <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="pathValue"/> contains a non-empty hop that is not
        /// <see cref="NntpNewsLogFeedNames.NotForMail"/>; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para><b>Rejection cases:</b></para>
        /// <list type="bullet">
        /// <item><description><paramref name="pathValue"/> is empty.</description></item>
        /// <item>
        /// <description>
        /// The hop before the bang (or the whole value when no bang exists) is empty after ASCII trim.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// The hop equals <see cref="NntpNewsLogFeedNames.NotForMail"/> under
        /// <see cref="StringComparison.OrdinalIgnoreCase"/> (including values such as
        /// <c>not-for-mail!real.host</c> where the first hop itself is <c>not-for-mail</c>).
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Exposed for unit tests and for callers that already hold a Path value span without re-scanning headers.
        /// </para>
        /// </remarks>
        internal static bool TryExtractFirstHop(ReadOnlySpan<byte> pathValue, out string? firstHop)
        {
            firstHop = null;
            if (pathValue.IsEmpty)
            {
                return false;
            }

            int bang = pathValue.IndexOf((byte)'!');
            ReadOnlySpan<byte> hopBytes = bang < 0 ? pathValue : pathValue[..bang];
            hopBytes = TrimAscii(hopBytes);
            if (hopBytes.IsEmpty)
            {
                return false;
            }

            string hop = Encoding.ASCII.GetString(hopBytes);
            if (hop.Equals(NntpNewsLogFeedNames.NotForMail, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            firstHop = hop;
            return true;
        }

        /// <summary>
        /// Trims ASCII horizontal whitespace from both ends of a byte span without allocating.
        /// </summary>
        /// <param name="value">Input bytes to trim.</param>
        /// <returns>
        /// A sub-span of <paramref name="value"/> with leading and trailing space (U+0020) and tab (U+0009) removed.
        /// Returns an empty span when <paramref name="value"/> is empty or contains only whitespace.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Does not trim other Unicode whitespace categories. Path hop extraction intentionally mirrors the lightweight
        /// ASCII trim used elsewhere on the transit spool hot path.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        private static ReadOnlySpan<byte> TrimAscii(ReadOnlySpan<byte> value)
        {
            int start = 0;
            while (start < value.Length && value[start] is (byte)' ' or (byte)'\t')
            {
                start++;
            }

            int end = value.Length;
            while (end > start && value[end - 1] is (byte)' ' or (byte)'\t')
            {
                end--;
            }

            return value[start..end];
        }
    }
}
