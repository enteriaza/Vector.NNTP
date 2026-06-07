// <copyright file="NntpNewsFeedResolver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: INN news log incoming feed resolution from origin metadata and Path headers.

using Vector.NNTP.Articles.Storage;

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Resolves the INN news log feed field from spool origin metadata and optional article bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every accept, reject, cancel, and junk line written by <see cref="NntpNewsLog"/> includes a feed column identifying
    /// the incoming source. This type implements the resolution chain used before <see cref="NntpNewsLogFormatter"/> formats
    /// the line. Callers pass the <see cref="NntpSpoolArticleOrigin"/> captured at enqueue plus the article bytes available
    /// at log time (full payload on the writer pump path; often empty on early reject paths in
    /// <see cref="NntpSpoolTransitStorage"/>).
    /// </para>
    /// <para><b>Resolution priority:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// When <see cref="NntpSpoolArticleOrigin.IsLocalPost"/> is <see langword="true"/>, return
    /// <see cref="NntpNewsLogFeedNames.Local"/> (<c>local</c>) without consulting Path headers or peer hostnames.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// When <see cref="NntpSpoolArticleOrigin.TransitPeerName"/> is non-empty after trim, return that configured peer name
    /// (for example <c>Giganews</c>). This wins over Path and hostname fallbacks even when article bytes contain a
    /// different <c>Path</c> hop.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// When article bytes are non-empty, delegate to <see cref="PathHeaderFeedResolver.TryResolveFeed"/> for
    /// the first usable <c>Path</c> first hop (skipping <see cref="NntpNewsLogFeedNames.NotForMail"/>).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// When <see cref="NntpSpoolArticleOrigin.PeerHostName"/> is non-empty after trim, return the resolved peer FQDN.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Otherwise return <see cref="NntpNewsLogFeedNames.UnknownFeed"/> (<c>?</c>).
    /// </description>
    /// </item>
    /// </list>
    /// <para><b>Thread safety:</b> Static and stateless; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal static class NntpNewsFeedResolver
    {
        /// <summary>
        /// Resolves the feed token for an INN news log line from origin metadata and optional article bytes.
        /// </summary>
        /// <param name="origin">
        /// Peer origin captured when the article was enqueued on the spool write queue. Supplies local-post flag, configured
        /// transit peer name, and reverse-DNS hostname fallbacks.
        /// </param>
        /// <param name="articleBytes">
        /// Raw article bytes used for optional <c>Path</c> header lookup. May be empty when the caller rejects before a full
        /// payload is available or when Path-based resolution is not needed because higher-priority origin fields apply.
        /// </param>
        /// <returns>
        /// A non-empty feed token suitable for the feed column of an INN news line: <see cref="NntpNewsLogFeedNames.Local"/>,
        /// a trimmed transit peer name or peer hostname, a Path-derived first hop from
        /// <see cref="PathHeaderFeedResolver"/>, or <see cref="NntpNewsLogFeedNames.UnknownFeed"/> when no source can be
        /// determined.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Never throws. Whitespace-only <see cref="NntpSpoolArticleOrigin.TransitPeerName"/> and
        /// <see cref="NntpSpoolArticleOrigin.PeerHostName"/> values are treated as absent and resolution continues to the next
        /// step. Non-empty peer names and hostnames are returned with leading and trailing ASCII space and tab removed.
        /// </para>
        /// <para>
        /// Path lookup runs only when earlier steps fail and <paramref name="articleBytes"/> is not empty. An empty span skips
        /// Path parsing entirely rather than invoking <see cref="PathHeaderFeedResolver.TryResolveFeed"/>.
        /// </para>
        /// </remarks>
        internal static string ResolveFeed(in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            return origin.IsLocalPost
                ? NntpNewsLogFeedNames.Local
                : !string.IsNullOrWhiteSpace(origin.TransitPeerName)
                    ? origin.TransitPeerName.Trim()
                    : !articleBytes.IsEmpty &&
                        PathHeaderFeedResolver.TryResolveFeed(articleBytes, out string pathFeed)
                        ? pathFeed
                        : !string.IsNullOrWhiteSpace(origin.PeerHostName)
                            ? origin.PeerHostName.Trim()
                            : NntpNewsLogFeedNames.UnknownFeed;
        }
    }
}
