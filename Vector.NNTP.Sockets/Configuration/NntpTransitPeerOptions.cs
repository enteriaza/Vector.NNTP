// <copyright file="NntpTransitPeerOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: trusted transit peer definition for NNTPD peering.

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// One configured transit peer: stable identity, display name, address entries, and cluster connection cap.
    /// </summary>
    public sealed class NntpTransitPeerOptions
    {
        /// <summary>
        /// Gets or sets the stable peer identifier used for Redis keys and metrics (for example <c>giganews</c>).
        /// </summary>
        /// <remarks>
        /// Operators may rename <see cref="Name"/> without breaking dashboards; <see cref="PeerId"/> must remain stable.
        /// </remarks>
        [Required]
        public string PeerId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the human-readable peer name for logs (for example <c>Giganews Primary Feed</c>).
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the maximum cluster-wide concurrent connections for this peer.
        /// </summary>
        /// <remarks>
        /// <c>0</c> means unlimited. Positive values are enforced via Redis ZSET admission.
        /// </remarks>
        [Range(0, int.MaxValue)]
        public int AcceptMaxConnections { get; set; } = 10;

        /// <summary>
        /// Gets or sets address entries that identify this peer (literal IP, CIDR, or DNS hostname).
        /// </summary>
        public string[] AcceptFrom { get; set; } = Array.Empty<string>();
    }
}
