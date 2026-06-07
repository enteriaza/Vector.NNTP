// <copyright file="FakeNntpTransitStorage.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory transit storage for RFC 4644 tests.

using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Tests.Sockets.Fakes
{
    /// <summary>
    /// In-memory transit storage for CHECK/IHAVE/TAKETHIS protocol tests.
    /// </summary>
    internal sealed class FakeNntpTransitStorage : INntpTransitStorage
    {
        /// <summary>
        /// Message identifiers recorded as accepted by <see cref="TakeThisAsync"/>.
        /// </summary>
        private readonly HashSet<string> _have = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets the result returned by <see cref="TakeThisAsync"/>.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="NntpTransitStorageResult.Success"/>. Set to
        /// <see cref="NntpTransitStorageResult.ArticleRejected"/> or <see cref="NntpTransitStorageResult.QueueFull"/>
        /// to exercise handler response mapping without a live spool queue.
        /// </remarks>
        internal NntpTransitStorageResult TakeThisResult { get; set; } = NntpTransitStorageResult.Success;

        /// <summary>
        /// Returns whether the fake has not yet recorded the message identifier.
        /// </summary>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="cancellationToken">Cancellation token (ignored).</param>
        /// <returns><see langword="true"/> when <paramref name="messageId"/> is not in the accepted set.</returns>
        public ValueTask<bool> CheckAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(!this._have.Contains(messageId));
        }

        /// <summary>
        /// Returns whether the fake has not yet recorded the message identifier.
        /// </summary>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="cancellationToken">Cancellation token (ignored).</param>
        /// <returns><see langword="true"/> when <paramref name="messageId"/> is not in the accepted set.</returns>
        public ValueTask<bool> IHaveAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(!this._have.Contains(messageId));
        }

        /// <summary>
        /// Records the message identifier when configured to return <see cref="NntpTransitStorageResult.Success"/>.
        /// </summary>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="articleBytes">Article bytes (ignored).</param>
        /// <param name="origin">Peer origin metadata (ignored).</param>
        /// <param name="cancellationToken">Cancellation token (ignored).</param>
        /// <returns><see cref="TakeThisResult"/>.</returns>
        public ValueTask<NntpTransitStorageResult> TakeThisAsync(
            string messageId,
            ReadOnlyMemory<byte> articleBytes,
            NntpTransitArticleOrigin origin,
            CancellationToken cancellationToken)
        {
            _ = articleBytes;
            _ = origin;
            _ = cancellationToken;
            if (this.TakeThisResult == NntpTransitStorageResult.Success)
            {
                this._have.Add(messageId);
            }

            return ValueTask.FromResult(this.TakeThisResult);
        }
    }
}
