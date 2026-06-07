// <copyright file="NntpSpoolArticleOrigin.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: spool queue copy of transit reception metadata carried on every enqueued article for Tier-2 spamd synthesis.

using System.Net;

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Reception metadata stored on <see cref="NntpSpoolWriteItem"/> for programmatic SpamAssassin header synthesis.
    /// </summary>
    /// <param name="PeerAddress">
    /// Effective NNTP peer IP address captured at enqueue. Never <see langword="null"/>; sourced from
    /// <see cref="Vector.NNTP.Sockets.Storage.NntpTransitArticleOrigin.PeerAddress"/>.
    /// </param>
    /// <param name="PeerHostName">
    /// Resolved peer FQDN when <see cref="Vector.NNTP.Sockets.Policy.NntpTransitPeerHostnameResolver"/> succeeds;
    /// otherwise <see langword="null"/> (spamd <c>Received:</c> synthesis falls back to IP-only wording).
    /// </param>
    /// <param name="ReceivedUtc">
    /// UTC reception timestamp captured at enqueue, used for mail-style <c>Received:</c> and default <c>Date:</c> synthesis.
    /// </param>
    /// <remarks>
    /// <para>
    /// Copied from <see cref="Vector.NNTP.Sockets.Storage.NntpTransitArticleOrigin"/> when
    /// <see cref="NntpSpoolTransitStorage"/> accepts an article. The struct is stored on every
    /// <see cref="NntpSpoolWriteItem"/> regardless of whether spam checking runs later.
    /// </para>
    /// <para>
    /// Consumed on the writer path by <see cref="Processing.SpamdScanArticleBuilder"/> when
    /// <see cref="Processing.ArticleSpoolPostprocessor"/> elects a spam scan for eligible articles.
    /// </para>
    /// <para><b>Thread safety:</b> Immutable value type; safe to read from concurrent writer pumps after enqueue.</para>
    /// </remarks>
    public readonly record struct NntpSpoolArticleOrigin(
        IPAddress PeerAddress,
        string? PeerHostName,
        DateTimeOffset ReceivedUtc)
    {
        /// <summary>
        /// Creates a spool origin snapshot from a transit storage origin value.
        /// </summary>
        /// <param name="origin">Origin captured at TAKETHIS/IHAVE enqueue.</param>
        /// <returns>Equivalent spool queue origin struct with the same peer address, hostname, and UTC timestamp.</returns>
        /// <remarks>
        /// Does not clone or resolve hostnames; copies fields verbatim from the transit-layer snapshot. Never throws.
        /// </remarks>
        public static NntpSpoolArticleOrigin FromTransit(Vector.NNTP.Sockets.Storage.NntpTransitArticleOrigin origin)
        {
            return new NntpSpoolArticleOrigin(origin.PeerAddress, origin.PeerHostName, origin.ReceivedUtc);
        }
    }
}
