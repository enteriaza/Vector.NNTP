// <copyright file="NntpSpoolArticleOrigin.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: spool queue copy of transit reception metadata carried on every enqueued article for Tier-2 spamd synthesis.

using System.Net;
using Vector.NNTP.Sockets.Policy;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Immutable reception metadata stored on <see cref="NntpSpoolWriteItem"/> for news-log feed resolution, spool
    /// metrics rollups, and optional SpamAssassin scan header synthesis on the writer path.
    /// </summary>
    /// <param name="PeerAddress">
    /// Effective NNTP peer IP address captured at enqueue (post-PROXY when applicable). Never <see langword="null"/>.
    /// Sourced from <see cref="NntpTransitArticleOrigin.PeerAddress"/>. Used by
    /// <see cref="Processing.SpamdScanArticleBuilder"/> for bracketed IP wording in synthetic <c>Received:</c> headers
    /// when <paramref name="PeerHostName"/> is absent.
    /// </param>
    /// <param name="PeerHostName">
    /// Public FQDN peer hostname when <see cref="NntpTransitPeerHostnameResolver"/> succeeds at socket enqueue time;
    /// otherwise <see langword="null"/>. Used by <see cref="Processing.SpamdScanArticleBuilder"/> for
    /// <c>from host (host [ip])</c> <c>Received:</c> wording and as a fallback feed token in
    /// <see cref="Logging.NntpNewsFeedResolver"/> when higher-priority origin fields and <c>Path</c> headers do not apply.
    /// </param>
    /// <param name="ReceivedUtc">
    /// UTC timestamp when the article body was accepted for spool enqueue. Used for mail-style <c>Received:</c> date
    /// clauses in <see cref="Processing.SpamdScanArticleBuilder"/> and for default <c>Date:</c> synthesis when the
    /// original article lacks a <c>Date</c> header.
    /// </param>
    /// <param name="TransitPeerName">
    /// Configured transit peer name when the connection was admitted as a trusted peer (for example <c>Giganews</c>);
    /// otherwise <see langword="null"/>. Defaults to <see langword="null"/> when omitted. Wins over <c>Path</c> and
    /// <paramref name="PeerHostName"/> in <see cref="Logging.NntpNewsFeedResolver"/> feed resolution.
    /// </param>
    /// <param name="IsLocalPost">
    /// When <see langword="true"/>, the article originated from a local reader POST rather than a transit peer feed.
    /// Defaults to <see langword="false"/> when omitted. Causes <see cref="Logging.NntpNewsFeedResolver"/> to emit feed
    /// <see cref="Logging.NntpNewsLogFeedNames.Local"/> without consulting Path headers or peer hostnames.
    /// </param>
    /// <remarks>
    /// <para><b>Role:</b> Articles-layer snapshot of socket-layer <see cref="NntpTransitArticleOrigin"/>. Keeps peer DNS
    /// resolution on the protocol hot path while allowing writer pumps to synthesize spamd scan copies and INN news lines
    /// without re-querying connection state.</para>
    /// <para><b>Lifecycle:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// TAKETHIS/IHAVE handlers build <see cref="NntpTransitArticleOrigin"/> (typically via
    /// <see cref="NntpTransitArticleOrigin.CreateFromConnectionAsync"/>) before calling
    /// <see cref="NntpSpoolTransitStorage.TakeThisAsync"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="FromTransit"/> copies fields verbatim into <see cref="NntpSpoolWriteItem.Origin"/> on every accepted
    /// enqueue.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriterPump"/> and <see cref="NntpSpoolTransitStorage"/> pass the struct to
    /// <see cref="Metrics.NntpSpoolMetrics"/> and <see cref="Logging.INntpNewsLog"/> on accept/reject paths; eligible
    /// articles additionally flow through <see cref="Processing.SpamdScanArticleBuilder"/> during postprocessing.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Stored on every <see cref="NntpSpoolWriteItem"/> regardless of whether spam checking, Path-header feed resolution,
    /// or minute throughput rollups need every field for a given article.
    /// </para>
    /// <para>
    /// Positional record semantics expose all parameters as init-only properties with synthesized equality. The type
    /// carries no behavior beyond <see cref="FromTransit"/> and does not validate field contents at construction.
    /// </para>
    /// <para><b>Thread safety:</b> Immutable value type; safe to read from concurrent writer pumps after enqueue.</para>
    /// </remarks>
    public readonly record struct NntpSpoolArticleOrigin(
        IPAddress PeerAddress,
        string? PeerHostName,
        DateTimeOffset ReceivedUtc,
        string? TransitPeerName = null,
        bool IsLocalPost = false)
    {
        /// <summary>
        /// Creates a spool-queue origin snapshot from a transit-layer origin value.
        /// </summary>
        /// <param name="origin">
        /// Origin captured at TAKETHIS/IHAVE enqueue on the socket path, including any reverse-DNS hostname resolution
        /// performed before body transfer.
        /// </param>
        /// <returns>
        /// A <see cref="NntpSpoolArticleOrigin"/> with the same <see cref="PeerAddress"/>,
        /// <see cref="PeerHostName"/>, <see cref="ReceivedUtc"/>, <see cref="TransitPeerName"/>, and
        /// <see cref="IsLocalPost"/> values as <paramref name="origin"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Field-wise copy with no cloning, hostname re-resolution, or timestamp adjustment. Reference types
        /// (<see cref="IPAddress"/> and strings) are shared by reference with <paramref name="origin"/>; callers must
        /// treat both structs as immutable after creation.
        /// </para>
        /// <para>
        /// Invoked from <see cref="NntpSpoolTransitStorage.TakeThisAsync"/> for every storage attempt that passes the
        /// size gate. Never throws.
        /// </para>
        /// </remarks>
        public static NntpSpoolArticleOrigin FromTransit(NntpTransitArticleOrigin origin)
        {
            return new NntpSpoolArticleOrigin(
                origin.PeerAddress,
                origin.PeerHostName,
                origin.ReceivedUtc,
                origin.TransitPeerName,
                origin.IsLocalPost);
        }
    }
}
