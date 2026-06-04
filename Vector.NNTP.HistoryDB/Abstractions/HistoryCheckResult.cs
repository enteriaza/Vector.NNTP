// <copyright file="HistoryCheckResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Abstractions
{
    /// <summary>
    /// Outcome of a transit CHECK against the history database (RFC 4644 mapping in Sockets).
    /// </summary>
    public enum HistoryCheckResult
    {
        /// <summary>
        /// Article wanted — not a duplicate in retention window (238); read-only probe, no record.
        /// </summary>
        Wanted,

        /// <summary>
        /// Duplicate within the retention window (438).
        /// </summary>
        Duplicate,

        /// <summary>
        /// Transient failure after the subsystem is operational (431).
        /// </summary>
        TryAgainLater,

        /// <summary>
        /// Subsystem not operational — startup or mandatory rebuild not finished (503).
        /// </summary>
        Unavailable,
    }
}
