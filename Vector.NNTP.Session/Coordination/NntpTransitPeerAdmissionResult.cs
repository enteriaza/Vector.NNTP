// <copyright file="NntpTransitPeerAdmissionResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Outcome of cluster-wide transit peer connection admission.
    /// </summary>
    public enum NntpTransitPeerAdmissionResult
    {
        /// <summary>Admission slot acquired in the peer ZSET.</summary>
        Success = 0,

        /// <summary>Peer is at configured capacity after stale purge.</summary>
        AtCapacity,

        /// <summary>Redis or coordination backend failure.</summary>
        BackendFailure,
    }
}
