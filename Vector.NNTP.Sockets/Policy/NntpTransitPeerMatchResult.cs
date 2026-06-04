// <copyright file="NntpTransitPeerMatchResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: result of matching a client IP to a configured transit peer.

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>
    /// Outcome of matching a client address against the transit peer snapshot.
    /// </summary>
    public readonly struct NntpTransitPeerMatchResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NntpTransitPeerMatchResult"/> struct.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="displayName">Operator display name.</param>
        /// <param name="matchedEntry">Configuration entry that matched.</param>
        /// <param name="maxConnections">Configured <c>AcceptMaxConnections</c> (0 = unlimited).</param>
        public NntpTransitPeerMatchResult(string peerId, string displayName, string matchedEntry, int maxConnections)
        {
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(displayName);
            ArgumentException.ThrowIfNullOrEmpty(matchedEntry);
            PeerId = peerId;
            DisplayName = displayName;
            MatchedEntry = matchedEntry;
            MaxConnections = maxConnections;
        }

        /// <summary>
        /// Gets the stable peer identifier for Redis and metrics.
        /// </summary>
        public string PeerId { get; }

        /// <summary>
        /// Gets the display name for logs.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the configuration entry that matched (literal, CIDR, or hostname text).
        /// </summary>
        public string MatchedEntry { get; }

        /// <summary>
        /// Gets the configured maximum cluster connections (0 = unlimited).
        /// </summary>
        public int MaxConnections { get; }
    }
}
