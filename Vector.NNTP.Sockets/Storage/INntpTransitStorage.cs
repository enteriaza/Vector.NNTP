// <copyright file="INntpTransitStorage.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC 4644 transit storage contract.

namespace Vector.NNTP.Sockets.Storage
{
    /// <summary>
    /// Transit streaming storage for CHECK, IHAVE, and TAKETHIS (RFC 4644).
    /// </summary>
    public interface INntpTransitStorage
    {
        /// <summary>
        /// Checks whether the server wants an article with the given message-id.
        /// </summary>
        /// <param name="messageId">Message-ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when the server wants the article (438 otherwise).</returns>
        ValueTask<bool> CheckAsync(string messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Accepts an IHAVE offer; returns whether to send the article body.
        /// </summary>
        /// <param name="messageId">Message-ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when client should send body (335).</returns>
        ValueTask<bool> IHaveAsync(string messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Stores an article received via TAKETHIS or IHAVE body.
        /// </summary>
        /// <param name="messageId">Message-ID.</param>
        /// <param name="articleBytes">Article bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> on success (235).</returns>
        ValueTask<bool> TakeThisAsync(string messageId, ReadOnlyMemory<byte> articleBytes, CancellationToken cancellationToken);
    }
}
