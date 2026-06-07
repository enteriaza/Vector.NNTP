// <copyright file="NntpTransitArticleOrigin.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: peer identity captured at TAKETHIS/IHAVE enqueue for downstream spamd scan synthesis.

namespace Vector.NNTP.Sockets.Storage
{
    /// <summary>
    /// Immutable transit article reception metadata captured when a streaming body is accepted for spool enqueue.
    /// </summary>
    /// <param name="PeerAddress">Effective NNTP peer IP address (post-PROXY).</param>
    /// <param name="PeerHostName">
    /// Public FQDN peer hostname when resolved from AcceptFrom or reverse DNS; <see langword="null"/> when only IP is known.
    /// </param>
    /// <param name="ReceivedUtc">UTC timestamp when the article body was accepted for enqueue.</param>
    /// <param name="TransitPeerName">
    /// Configured transit peer name when the connection was admitted as a trusted peer; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="IsLocalPost">
    /// When <see langword="true"/>, the article originated from a local reader POST rather than a transit peer feed.
    /// </param>
    /// <remarks>
    /// <para>
    /// Passed from TAKETHIS/IHAVE handlers into <see cref="INntpTransitStorage.TakeThisAsync"/> so the spool writer can
    /// synthesize truthful SpamAssassin <c>Received:</c> headers without blocking the socket hot path with DNS work
    /// beyond a single bounded reverse lookup per article.
    /// </para>
    /// </remarks>
    public readonly record struct NntpTransitArticleOrigin(
        IPAddress PeerAddress,
        string? PeerHostName,
        DateTimeOffset ReceivedUtc,
        string? TransitPeerName = null,
        bool IsLocalPost = false)
    {
        /// <summary>
        /// Captures peer identity from an active session at article enqueue time.
        /// </summary>
        /// <param name="connection">Connection accepting the article body.</param>
        /// <param name="cancellationToken">Cancellation token for bounded reverse DNS.</param>
        /// <returns>Origin metadata for <see cref="INntpTransitStorage.TakeThisAsync"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connection"/> is <see langword="null"/>.</exception>
        public static async ValueTask<NntpTransitArticleOrigin> CreateFromConnectionAsync(
            Session.NntpConnectionContext connection,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            string? peerHostName = await Policy.NntpTransitPeerHostnameResolver
                .ResolveAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            return new NntpTransitArticleOrigin(
                connection.ClientRemoteEndPoint.Address,
                peerHostName,
                DateTimeOffset.UtcNow,
                connection.TransitPeerName,
                IsLocalPost: false);
        }
    }
}
