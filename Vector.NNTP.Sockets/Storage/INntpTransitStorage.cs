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
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// <see langword="true"/> when storage accepted the article. Command handlers map this to
        /// <c>235 Article transferred OK</c> for IHAVE and <c>239 Article transferred OK</c> for TAKETHIS.
        /// </returns>
        public ValueTask<bool> TakeThisAsync(string messageId, ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken);
    }
}
