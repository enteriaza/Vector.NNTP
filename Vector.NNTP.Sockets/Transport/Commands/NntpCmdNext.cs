// <copyright file="NntpCmdNext.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: NEXT command handler.

using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP NEXT command.
    /// </summary>
    internal static class NntpCmdNext
    {
        /// <summary>
        /// Advances the current article pointer to the next article in the selected group.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="storage">Article storage (may be null).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            INntpArticleStorage? storage,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (string.IsNullOrEmpty(session.State.SelectedGroup))
            {
                await NntpReaderErrors.WriteNoGroupSelected412(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (storage is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (session.State.CurrentArticleNumber is not long current)
            {
                await session.Writer.WriteLineAsync("420 No current article has been selected", cancellationToken).ConfigureAwait(false);
                return true;
            }

            long? next = await storage.GetNextArticleNumberAsync(session.State.SelectedGroup, current, cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (next.Value < 0)
            {
                await session.Writer.WriteLineAsync("421 No next article in this group", cancellationToken).ConfigureAwait(false);
                return true;
            }

            return await WriteArticlePointerAsync(session, storage, next.Value, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the article pointer to the session writer.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="storage">The article storage.</param>
        /// <param name="articleNumber">The article number.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the article pointer is written.</returns>
        private static async ValueTask<bool> WriteArticlePointerAsync(
            NntpSession session,
            INntpArticleStorage storage,
            long articleNumber,
            CancellationToken cancellationToken)
        {
            session.State.CurrentArticleNumber = articleNumber;
            string? messageId = await storage.GetArticleMessageIdAsync(
                session.State.SelectedGroup,
                articleNumber,
                null,
                cancellationToken).ConfigureAwait(false);
            messageId ??= "<unknown@local>";

            await session.Writer.WriteLineAsync($"223 {articleNumber} {messageId}", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
