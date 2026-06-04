// <copyright file="HistoryRecordResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Abstractions
{
    /// <summary>
    /// Outcome of recording a message-id on TAKETHIS/IHAVE accept (RFC 4644 mapping in Sockets).
    /// </summary>
    public enum HistoryRecordResult
    {
        /// <summary>
        /// Message-id was newly recorded in history (Redis SET NX succeeded).
        /// </summary>
        Recorded,

        /// <summary>
        /// Message-id already exists in the retention window.
        /// </summary>
        Duplicate,

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
