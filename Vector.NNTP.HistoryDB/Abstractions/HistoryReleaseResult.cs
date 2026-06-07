// <copyright file="HistoryReleaseResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Abstractions
{
    /// <summary>
    /// Outcome of releasing a message-id from history after spool preprocess or write failure.
    /// </summary>
    public enum HistoryReleaseResult
    {
        /// <summary>
        /// Message-id was removed from Redis, memory, and Rocks (best-effort across tiers).
        /// </summary>
        Released,

        /// <summary>
        /// Message-id was not present in history tiers.
        /// </summary>
        NotFound,

        /// <summary>
        /// Transient failure after the subsystem is operational.
        /// </summary>
        TryAgainLater,

        /// <summary>
        /// Subsystem not operational — startup or mandatory rebuild not finished.
        /// </summary>
        Unavailable,
    }
}
