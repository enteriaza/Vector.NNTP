// <copyright file="NntpTransitPeerOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: trusted transit peer definition for NNTPD peering.

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// One configured transit peer: name, address entries, and cluster connection cap.
    /// </summary>
    public sealed class NntpTransitPeerOptions
    {
        /// <summary>
        /// Gets or sets the peer name used for logs, metrics labels, and Redis coordination keys.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the maximum cluster-wide concurrent connections for this peer.
        /// </summary>
        /// <remarks>
        /// <c>0</c> means unlimited. Positive values are enforced via Redis ZSET admission on inbound <see cref="AcceptFrom"/> matches.
        /// </remarks>
        [Range(0, int.MaxValue)]
        public int MaxConnections { get; set; } = 10;

        /// <summary>
        /// Gets or sets address entries that identify this peer (literal IP, CIDR, or DNS hostname).
        /// </summary>
        public string[] AcceptFrom { get; set; } = Array.Empty<string>();
    }
}
