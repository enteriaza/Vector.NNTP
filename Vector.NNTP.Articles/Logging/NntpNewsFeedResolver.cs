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
    /// <b>Role:</b> Single entry point for the incoming <b>feed</b> column on INN <c>pathlog/news</c> lines and matching
    /// OpenTelemetry <c>feed</c> tags. <see cref="NntpNewsLog"/> and
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleAccepted"/> /
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleRejected"/> call <see cref="ResolveFeed"/> before formatting or
    /// incrementing counters so logs and metrics stay aligned.
    /// </para>
    /// <para>
    /// Callers pass <see cref="NntpSpoolArticleOrigin"/> captured at enqueue plus article bytes available at log or metrics
    /// time. Resolution runs immediately before <see cref="NntpNewsLogFormatter"/> builds the line text.
    /// </para>
    /// <para><b>Resolution priority (first match wins):</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// When <see cref="NntpSpoolArticleOrigin.IsLocalPost"/> is <see langword="true"/>, return
    /// <see cref="NntpNewsLogFeedNames.Local"/> (<c>local</c>). Highest priority — Path headers, transit peer names, and
    /// hostnames are not consulted even when present on the origin or in article bytes.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// When <see cref="NntpSpoolArticleOrigin.TransitPeerName"/> is non-empty after
    /// <see cref="string.IsNullOrWhiteSpace"/> check, return that name with leading and trailing whitespace removed (for example
    /// <c>Giganews</c>). Wins over Path and <see cref="NntpSpoolArticleOrigin.PeerHostName"/> even when article bytes
    /// contain a different <c>Path</c> hop.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// When article bytes are non-empty, delegate to <see cref="PathHeaderFeedResolver.TryResolveFeed"/> for the first
    /// usable <c>Path</c> first hop (skipping <see cref="NntpNewsLogFeedNames.NotForMail"/>).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// When <see cref="NntpSpoolArticleOrigin.PeerHostName"/> is non-empty after
    /// <see cref="string.IsNullOrWhiteSpace"/> check, return that hostname with leading and trailing whitespace removed.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Otherwise return <see cref="NntpNewsLogFeedNames.UnknownFeed"/> (<c>?</c>).
    /// </description>
    /// </item>
    /// </list>
    /// <para><b>Caller article-byte snapshots (production):</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Enqueue rejections in <see cref="NntpSpoolTransitStorage"/> — often empty bytes; Path step is skipped.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Preprocess failures in <see cref="NntpSpoolWriterPump"/> — original enqueued payload (no
    /// <see cref="Processing.ArticlePathHeaderMutator"/> rewrite).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Postprocess failures, write failures, and successful commits — preprocess output (first <c>Path</c> hop may already
    /// include local <see cref="Sockets.Configuration.NntpServerOptions.PathAppend"/> when configured).
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
        /// <see cref="NntpSpoolArticleOrigin"/> captured when the article was enqueued on the spool write queue. Supplies
        /// <see cref="NntpSpoolArticleOrigin.IsLocalPost"/>, configured
        /// <see cref="NntpSpoolArticleOrigin.TransitPeerName"/>, and reverse-DNS
        /// <see cref="NntpSpoolArticleOrigin.PeerHostName"/> fallbacks. Immutable for the lifetime of the queue item.
        /// </param>
        /// <param name="articleBytes">
        /// Raw article bytes used for optional <c>Path</c> header lookup at step 3 of the resolution chain. May be empty
        /// when the caller rejects before retaining a full payload or when steps 1–2 already determined the feed. Never
        /// mutated by this method.
        /// </param>
        /// <returns>
        /// A non-empty feed token suitable for the feed column of an INN news line and for OpenTelemetry <c>feed</c> tags:
        /// <see cref="NntpNewsLogFeedNames.Local"/>; a trimmed <see cref="NntpSpoolArticleOrigin.TransitPeerName"/>; a
        /// Path-derived first hop from <see cref="PathHeaderFeedResolver"/>; a trimmed
        /// <see cref="NntpSpoolArticleOrigin.PeerHostName"/>; or <see cref="NntpNewsLogFeedNames.UnknownFeed"/> when no
        /// source can be determined.
        /// </returns>
        /// <remarks>
        /// <para><b>Never throws.</b> Malformed article bytes cause Path resolution to fail without exception; resolution
        /// continues to hostname fallback or <see cref="NntpNewsLogFeedNames.UnknownFeed"/>.
        /// </para>
        /// <para>
        /// Whitespace-only <see cref="NntpSpoolArticleOrigin.TransitPeerName"/> and
        /// <see cref="NntpSpoolArticleOrigin.PeerHostName"/> values are treated as absent via
        /// <see cref="string.IsNullOrWhiteSpace"/> and resolution continues to the next step.
        /// </para>
        /// <para>
        /// Path lookup runs only when steps 1–2 do not produce a feed and <paramref name="articleBytes"/> is not empty. An
        /// empty span skips <see cref="PathHeaderFeedResolver.TryResolveFeed"/> entirely rather than invoking it with no
        /// data.
        /// </para>
        /// <para>
        /// Does not allocate when steps 1–2 or 5 match (constant or trimmed existing strings). Path success allocates the
        /// hop string inside <see cref="PathHeaderFeedResolver"/>.
        /// </para>
        /// </remarks>
        /// <example>
        /// A transit peer named <c>Giganews</c> with article <c>Path: other.example.com</c> resolves to
        /// <c>Giganews</c> because transit peer name outranks Path headers.
        /// </example>
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
