// <copyright file="INntpTransitStorage.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC 4644 transit storage contract.

namespace Vector.NNTP.Sockets.Storage
{
    /// <summary>
    /// Transit streaming storage for IHAVE and TAKETHIS article bodies (RFC 4644).
    /// </summary>
    /// <remarks>
    /// Duplicate filtering for streaming uses <c>Vector.NNTP.HistoryDB</c> (<c>CheckAsync</c> on CHECK,
    /// <c>TryRecordAsync</c> on TAKETHIS/IHAVE accept). This contract does not implement CHECK semantics.
    /// </remarks>
    public interface INntpTransitStorage
    {
        /// <summary>
        /// Legacy stub hook; production CHECK uses HistoryDB instead of transit storage.
        /// </summary>
        /// <param name="messageId">Message-ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when the stub wants the article.</returns>
        public ValueTask<bool> CheckAsync(string messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Accepts an IHAVE offer; returns whether to send the article body.
        /// </summary>
        /// <param name="messageId">Message-ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when client should send body (335).</returns>
        public ValueTask<bool> IHaveAsync(string messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Stores an article received via TAKETHIS or IHAVE body.
        /// </summary>
        /// <param name="messageId">Message-ID.</param>
        /// <param name="articleBytes">Article bytes.</param>
        /// <param name="origin">Peer identity and reception timestamp captured at enqueue.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// <see cref="NntpTransitStorageResult.Success"/> maps to <c>235</c>/<c>239</c>;
        /// <see cref="NntpTransitStorageResult.QueueFull"/> maps to <c>437</c> (IHAVE) or <c>439</c> (TAKETHIS);
        /// <see cref="NntpTransitStorageResult.ArticleRejected"/> maps to <c>437</c> (IHAVE) or <c>439</c> (TAKETHIS).
        /// </returns>
        public ValueTask<NntpTransitStorageResult> TakeThisAsync(
            string messageId,
            ReadOnlyMemory<byte> articleBytes,
            NntpTransitArticleOrigin origin,
            CancellationToken cancellationToken);
    }
}
