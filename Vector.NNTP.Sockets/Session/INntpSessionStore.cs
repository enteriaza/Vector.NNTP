// <copyright file="INntpSessionStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: optional node-local session registry.

namespace Vector.NNTP.Sockets.Session
{
    /// <summary>
    /// Optional node-local registry of active sessions (for operator tools; not Redis-coupled).
    /// </summary>
    public interface INntpSessionStore
    {
        /// <summary>
        /// Registers a session when a connection is accepted.
        /// </summary>
        /// <param name="session">Active session.</param>
        void Register(NntpSession session);

        /// <summary>
        /// Removes a session on teardown.
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        void Unregister(string sessionId);
    }
}
