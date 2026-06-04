// <copyright file="IHistoryDatabase.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Abstractions
{
    /// <summary>
    /// Transit history duplicate detection (CHECK) and recording (TAKETHIS/IHAVE accept).
    /// </summary>
    public interface IHistoryDatabase
    {
        /// <summary>
        /// Read-only duplicate probe for CHECK (no Redis/Rocks/memory write on wanted).
        /// </summary>
        /// <param name="messageId">Wire-validated message-id (caller must validate syntax).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>CHECK outcome for RFC response mapping (238/438/431/503).</returns>
        ValueTask<HistoryCheckResult> CheckAsync(string messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Atomically records a message-id when TAKETHIS/IHAVE accept proceeds (SET NX + async Rocks).
        /// </summary>
        /// <param name="messageId">Wire-validated message-id.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Record outcome for RFC response mapping.</returns>
        ValueTask<HistoryRecordResult> TryRecordAsync(string messageId, CancellationToken cancellationToken);
    }
}
