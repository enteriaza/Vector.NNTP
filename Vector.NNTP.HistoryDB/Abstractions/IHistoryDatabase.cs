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
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public ValueTask<HistoryCheckResult> CheckAsync(string messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Atomically records a message-id when TAKETHIS/IHAVE accept proceeds (SET NX + async Rocks).
        /// </summary>
        /// <param name="messageId">Wire-validated message-id.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Record outcome for RFC response mapping.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public ValueTask<HistoryRecordResult> TryRecordAsync(string messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Releases a message-id from all history tiers so the article may be offered again after spool failure.
        /// </summary>
        /// <param name="messageId">Wire-validated message-id.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// <see cref="HistoryReleaseResult.Released"/> when Redis, memory, persist tombstone, and Rocks delete succeed;
        /// <see cref="HistoryReleaseResult.NotFound"/> when no tier held the digest; transient or unavailable outcomes otherwise.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Full-tier release for spool preprocess/write failure recovery. Tombstones block in-flight persist queue items;
        /// tombstone entries are cleared after Rocks delete succeeds. Process restart may retain tombstones until natural
        /// expiration — acceptable because Redis and memory are already cleared.
        /// </para>
        /// </remarks>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public ValueTask<HistoryReleaseResult> TryReleaseAsync(string messageId, CancellationToken cancellationToken);
    }
}
