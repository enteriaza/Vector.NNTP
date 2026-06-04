// <copyright file="INntpTransitPeerMatcher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: transit peer address matching contract.

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>
    /// Matches effective client IP addresses against the current trusted transit peer snapshot.
    /// </summary>
    public interface INntpTransitPeerMatcher
    {
        /// <summary>
        /// Attempts to match <paramref name="clientAddress"/> against configured transit peers.
        /// </summary>
        /// <param name="clientAddress">Effective client IP (post-PROXY).</param>
        /// <param name="result">Match details when this method returns <see langword="true"/>.</param>
        /// <returns><see langword="true"/> when the address matches exactly one peer.</returns>
        bool TryMatch(IPAddress clientAddress, out NntpTransitPeerMatchResult result);
    }
}
