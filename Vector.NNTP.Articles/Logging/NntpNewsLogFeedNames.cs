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
    /// INN news lines separate the <b>feed</b> field (incoming source identity on plus, minus, cancel, and junk lines)
    /// from the trailing <b>site</b> list on plus and junk lines. This type centralizes literal tokens so formatters,
    /// feed resolvers, and Path parsing agree on spelling and placeholder semantics.
    /// </para>
    /// <para>
    /// <see cref="UnknownFeed"/> and <see cref="UnknownSite"/> both use the character <c>?</c> but appear in different
    /// columns of the log line and are produced by different pipeline stages.
    /// </para>
    /// <para><b>Thread safety:</b> Immutable string constants; safe to read from any thread.</para>
    /// </remarks>
    internal static class NntpNewsLogFeedNames
    {
        /// <summary>
        /// Feed token written for articles whose origin is marked as a local reader POST.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returned by <see cref="NntpNewsFeedResolver.ResolveFeed"/> when
        /// <see cref="Storage.NntpSpoolArticleOrigin.IsLocalPost"/> is <see langword="true"/>, without consulting Path
        /// headers or peer hostnames. Matches INN convention for locally injected posts.
        /// </para>
        /// <para>Literal value: <c>local</c>.</para>
        /// </remarks>
        internal const string Local = "local";

        /// <summary>
        /// Path header hop token that must never be emitted as an INN news log feed name.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Many generated articles use <c>Path: not-for-mail</c> as a placeholder. <see cref="PathHeaderFeedResolver"/>
        /// rejects this hop (case-insensitive) and continues feed resolution down the fallback chain rather than logging
        /// <c>not-for-mail</c> as the feed field.
        /// </para>
        /// <para>
        /// Also used in tests to verify that values such as <c>not-for-mail!real.host</c> do not yield
        /// <c>real.host</c> when the hop before the bang is still <c>not-for-mail</c>.
        /// </para>
        /// <para>Literal value: <c>not-for-mail</c>.</para>
        /// </remarks>
        internal const string NotForMail = "not-for-mail";

        /// <summary>
        /// Downstream site-list placeholder appended on plus and junk lines until newsfeeds routing exists.
        /// </summary>
        /// <remarks>
        /// <para>
        /// INN accept and junk lines end with a site field listing downstream peers that received the article. Transit
        /// spool v1 has no newsfeeds integration, so <see cref="NntpNewsLogFormatter.FormatAccepted"/> and
        /// <see cref="NntpNewsLogFormatter.FormatJunked"/> always append this token as the final column.
        /// </para>
        /// <para>Literal value: <c>?</c> (question mark).</para>
        /// </remarks>
        internal const string UnknownSite = "?";

        /// <summary>
        /// Sentinel for an unresolved feed name or an unparseable cancel target Message-ID.
        /// </summary>
        /// <remarks>
        /// <para><b>Feed resolution:</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// Final fallback from <see cref="NntpNewsFeedResolver.ResolveFeed"/> when origin metadata and Path header
        /// parsing cannot determine an incoming feed.
        /// </description>
        /// </item>
        /// </list>
        /// <para><b>Cancel targets:</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// Passed to <see cref="NntpNewsLogFormatter.FormatCancelProcessed"/> by <see cref="NntpNewsLog"/> when
        /// <see cref="CancelControlHeaderParser"/> cannot extract a cancel target; the formatter emits
        /// <c>Cancelling ?</c> without angle brackets.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Literal value: <c>?</c> (question mark). Same character as <see cref="UnknownSite"/> but used in the feed or
        /// cancel-target context, not as the trailing site column on plus lines.
        /// </para>
        /// </remarks>
        internal const string UnknownFeed = "?";
    }
}
