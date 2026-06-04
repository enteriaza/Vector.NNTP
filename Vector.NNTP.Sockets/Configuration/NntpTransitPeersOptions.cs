// <copyright file="NntpTransitPeersOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: trusted transit peer subsystem configuration for NNTPD.

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// Trusted transit peer subsystem: DNS refresh interval and peer definitions for NNTPD peering.
    /// </summary>
    public sealed class NntpTransitPeersOptions
    {
        /// <summary>
        /// Gets or sets the interval in minutes between DNS snapshot rebuilds for hostname <see cref="NntpTransitPeerOptions.AcceptFrom"/> entries.
        /// </summary>
        [Range(1, 1440)]
        public int RefreshIntervalMinutes { get; set; } = 10;

        /// <summary>
        /// Gets or sets configured transit peers.
        /// </summary>
        public NntpTransitPeerOptions[] Peers { get; set; } = Array.Empty<NntpTransitPeerOptions>();
    }
}
