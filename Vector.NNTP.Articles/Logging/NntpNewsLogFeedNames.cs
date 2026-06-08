// <copyright file="NntpNewsLogFeedNames.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: INN news log feed/site token constants.

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Well-known feed, site, and sentinel tokens shared by INN-style <c>pathlog/news</c> formatters and resolvers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Centralizes literal spellings so <see cref="NntpNewsFeedResolver"/>,
    /// <see cref="PathHeaderFeedResolver"/>, <see cref="NntpNewsLogFormatter"/>, and
    /// <see cref="Metrics.NntpSpoolMetrics"/> outcome counters agree on placeholder and sentinel semantics. Literal values
    /// are part of the external log and metrics contract — change only with operator and dashboard migration.
    /// </para>
    /// <para>
    /// INN <c>news</c> lines separate the <b>feed</b> column (incoming source on plus, minus, cancel, and junk lines)
    /// from the trailing <b>site</b> list on plus and junk lines only. Example accept shape:
    /// <c>{time} + {feed} &lt;message-id&gt; {bytes} {sites}</c>. Minus and cancel lines omit the site column.
    /// </para>
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    /// <item><description><see cref="NntpNewsFeedResolver"/> — emits <see cref="Local"/> and <see cref="UnknownFeed"/>.</description></item>
    /// <item><description><see cref="PathHeaderFeedResolver"/> — filters <see cref="NotForMail"/> during Path lookup.</description></item>
    /// <item>
    /// <description>
    /// <see cref="NntpNewsLogFormatter"/> — appends <see cref="UnknownSite"/> on plus and junk lines; passes
    /// <see cref="UnknownFeed"/> through for unparseable cancel targets.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpNewsLog"/> — substitutes <see cref="UnknownFeed"/> when
    /// <see cref="CancelControlHeaderParser"/> cannot extract a cancel target.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleAccepted"/> and
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleRejected"/> — reuse feed names from
    /// <see cref="NntpNewsFeedResolver"/> (including <see cref="Local"/> and <see cref="UnknownFeed"/>).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <see cref="UnknownFeed"/> and <see cref="UnknownSite"/> both use the character <c>?</c> but appear in different
    /// columns and contexts. Operators distinguish them by line prefix and position, not by spelling.
    /// </para>
    /// <para><b>Thread safety:</b> Immutable string constants; safe to read from any thread.</para>
    /// </remarks>
    internal static class NntpNewsLogFeedNames
    {
        /// <summary>
        /// Feed token written for articles whose origin is marked as a local reader POST.
        /// </summary>
        /// <value>Literal <c>local</c>.</value>
        /// <remarks>
        /// <para>
        /// <b>Producer:</b> <see cref="NntpNewsFeedResolver.ResolveFeed"/> when
        /// <see cref="Storage.NntpSpoolArticleOrigin.IsLocalPost"/> is <see langword="true"/>. This is the highest-priority
        /// feed resolution step; Path headers and peer hostnames are not consulted.
        /// </para>
        /// <para>
        /// <b>Consumers:</b> <see cref="NntpNewsLogFormatter"/> feed column on plus, minus, cancel, and junk lines;
        /// <see cref="Metrics.NntpSpoolMetrics"/> <c>feed</c> tag and per-feed minute throughput buckets.
        /// </para>
        /// <para>Matches INN convention for locally injected reader posts.</para>
        /// </remarks>
        internal const string Local = "local";

        /// <summary>
        /// Path header hop token that must never be emitted as an INN news log feed name.
        /// </summary>
        /// <value>Literal <c>not-for-mail</c>.</value>
        /// <remarks>
        /// <para>
        /// <b>Role:</b> Filter sentinel, not a feed output value. Many generated articles use
        /// <c>Path: not-for-mail</c> as a placeholder first hop.
        /// </para>
        /// <para>
        /// <b>Producer:</b> None at runtime — this constant is compared against Path hops in
        /// <see cref="PathHeaderFeedResolver.TryExtractFirstHop"/> (case-insensitive). Rejected hops cause Path resolution
        /// to continue scanning or fail so <see cref="NntpNewsFeedResolver"/> can fall back to
        /// <see cref="Storage.NntpSpoolArticleOrigin.PeerHostName"/> or <see cref="UnknownFeed"/>.
        /// </para>
        /// <para>
        /// Values such as <c>not-for-mail!real.host</c> do not yield <c>real.host</c> because only the hop before the
        /// first bang is considered and that hop is still <c>not-for-mail</c>.
        /// </para>
        /// <para>Never written to the <c>news</c> log file or metrics <c>feed</c> tag by production code.</para>
        /// </remarks>
        internal const string NotForMail = "not-for-mail";

        /// <summary>
        /// Downstream site-list placeholder appended as the final column on plus and junk lines until newsfeeds routing exists.
        /// </summary>
        /// <value>Literal <c>?</c> (single question mark).</value>
        /// <remarks>
        /// <para>
        /// <b>Role:</b> INN <b>site</b> column placeholder, not an incoming feed name. Accept and junk lines normally end
        /// with a list of downstream peers that received the article; transit spool v1 has no newsfeeds integration.
        /// </para>
        /// <para>
        /// <b>Producers:</b> <see cref="NntpNewsLogFormatter.FormatAccepted"/> and
        /// <see cref="NntpNewsLogFormatter.FormatJunked"/> always append this token as the final whitespace-separated field.
        /// </para>
        /// <para>
        /// <b>Not used on:</b> Minus (<c>-</c>) or cancel (<c>c</c>) lines, which have no trailing site column.
        /// </para>
        /// <para>
        /// Same character as <see cref="UnknownFeed"/> but appears only as the last field on plus/junk lines (for example
        /// <c>... + Giganews &lt;msg@id&gt; 842 ?</c>).
        /// </para>
        /// </remarks>
        internal const string UnknownSite = "?";

        /// <summary>
        /// Sentinel for an unresolved incoming feed name or an unparseable cancel target Message-ID.
        /// </summary>
        /// <value>Literal <c>?</c> (single question mark).</value>
        /// <remarks>
        /// <para>
        /// <b>Dual use:</b> The same literal serves two unrelated columns. Context on the log line disambiguates intent.
        /// </para>
        /// <para><b>Feed resolution (incoming source column):</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// Final fallback from <see cref="NntpNewsFeedResolver.ResolveFeed"/> when
        /// <see cref="Storage.NntpSpoolArticleOrigin"/> metadata and Path header parsing cannot determine an incoming feed
        /// (for example empty article bytes on enqueue rejection with no peer name or hostname).
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Appears as the feed field on plus, minus, cancel, and junk lines (for example
        /// <c>... - ? &lt;msg@id&gt; Rejected</c>).
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Propagates to OpenTelemetry <c>nntp.spool.article.accepted</c> / <c>rejected</c> <c>feed</c> tags and minute
        /// throughput buckets via <see cref="Metrics.NntpSpoolMetrics"/>.
        /// </description>
        /// </item>
        /// </list>
        /// <para><b>Cancel target (parsed Control header):</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// Substituted by <see cref="NntpNewsLog.LogCancelProcessed"/> when
        /// <see cref="CancelControlHeaderParser.TryParseCancelTarget"/> returns <see langword="false"/>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Passed to <see cref="NntpNewsLogFormatter.FormatCancelProcessed"/>, which emits
        /// <c>Cancelling ?</c> without angle brackets (contrast normalized targets such as
        /// <c>Cancelling &lt;target@example.com&gt;</c>).
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Not interchangeable with <see cref="UnknownSite"/>: <see cref="UnknownFeed"/> never fills the trailing site
        /// column on plus lines; <see cref="UnknownSite"/> does.
        /// </para>
        /// </remarks>
        internal const string UnknownFeed = "?";
    }
}
