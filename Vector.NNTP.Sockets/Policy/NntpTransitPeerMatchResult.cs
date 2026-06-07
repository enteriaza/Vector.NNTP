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
        /// <param name="name">Configured peer name.</param>
        /// <param name="matchedEntry">Configuration entry that matched.</param>
        /// <param name="maxConnections">Configured <c>MaxConnections</c> (0 = unlimited).</param>
        public NntpTransitPeerMatchResult(string name, string matchedEntry, int maxConnections)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentException.ThrowIfNullOrEmpty(matchedEntry);
            Name = name;
            MatchedEntry = matchedEntry;
            MaxConnections = maxConnections;
        }

        /// <summary>
        /// Gets the configured peer name for Redis and metrics.
        /// </summary>
        public string Name { get; }

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
