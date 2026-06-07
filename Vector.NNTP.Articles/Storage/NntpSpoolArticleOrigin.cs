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
    /// Reception metadata stored on <see cref="NntpSpoolWriteItem"/> for programmatic SpamAssassin header synthesis.
    /// </summary>
    /// <param name="PeerAddress">
    /// Effective NNTP peer IP address captured at enqueue. Never <see langword="null"/>; sourced from
    /// <see cref="NntpTransitArticleOrigin.PeerAddress"/>.
    /// </param>
    /// <param name="PeerHostName">
    /// Resolved peer FQDN when <see cref="NntpTransitPeerHostnameResolver"/> succeeds;
    /// otherwise <see langword="null"/> (spamd <c>Received:</c> synthesis falls back to IP-only wording).
    /// </param>
    /// <param name="ReceivedUtc">
    /// UTC reception timestamp captured at enqueue, used for mail-style <c>Received:</c> and default <c>Date:</c> synthesis.
    /// </param>
    /// <param name="TransitPeerName">
    /// Configured transit peer name when the connection was admitted as a trusted peer; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="IsLocalPost">
    /// When <see langword="true"/>, the article originated from a local reader POST rather than a transit peer feed.
    /// </param>
    /// <remarks>
    /// <para>
    /// Copied from <see cref="NntpTransitArticleOrigin"/> when
    /// <see cref="NntpSpoolTransitStorage"/> accepts an article. The struct is stored on every
    /// <see cref="NntpSpoolWriteItem"/> regardless of whether spam checking runs later.
    /// </para>
    /// <para>
    /// Consumed on the writer path by <see cref="Processing.SpamdScanArticleBuilder"/> when
    /// <see cref="Processing.ArticleSpoolPostprocessor"/> elects a spam scan for eligible articles, and by
    /// <see cref="Logging.NntpNewsFeedResolver"/> for INN news log feed resolution.
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
        /// Creates a spool origin snapshot from a transit storage origin value.
        /// </summary>
        /// <param name="origin">Origin captured at TAKETHIS/IHAVE enqueue.</param>
        /// <returns>Equivalent spool queue origin struct with the same peer address, hostname, and UTC timestamp.</returns>
        /// <remarks>
        /// Does not clone or resolve hostnames; copies fields verbatim from the transit-layer snapshot. Never throws.
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
