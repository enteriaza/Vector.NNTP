// <copyright file="NntpHostRole.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: host deployment role enumeration.

namespace Vector.NNTP.Sockets.HostProfile
{
    /// <summary>
    /// Deployment role for an NNTP connection (reader vs transit peering).
    /// </summary>
    public enum NntpHostRole
    {
        /// <summary>
        /// Reader host (NNRPD): MODE READER, GROUP/ARTICLE, POST when policy allows.
        /// </summary>
        Reader = 0,

        /// <summary>
        /// Transit host (NNTPD): MODE STREAM, CHECK/IHAVE/TAKETHIS per RFC 4644.
        /// </summary>
        Transit = 1,
    }
}
