// <copyright file="INntpSessionAdmissionTracker.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: optional session admission control after authentication.

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Tracks active NNTP sessions per account and source IP to enforce connection limits from <see cref="NntpSessionPolicy"/>.
    /// </summary>
    public interface INntpSessionAdmissionTracker
    {
        /// <summary>
        /// Attempts to admit a new session for the specified policy and client IP.
        /// </summary>
        /// <param name="policy">Granted session policy for the authenticated user.</param>
        /// <param name="clientIp">Effective client IP address (after PROXY resolution).</param>
        /// <returns><see langword="true"/> when the session is admitted.</returns>
        bool TryEnter(NntpSessionPolicy policy, IPAddress clientIp);

        /// <summary>
        /// Releases counters for a session that previously called <see cref="TryEnter"/>.
        /// </summary>
        /// <param name="policy">Session policy used during admission.</param>
        /// <param name="clientIp">Effective client IP address for the session.</param>
        void Leave(NntpSessionPolicy policy, IPAddress clientIp);
    }
}
