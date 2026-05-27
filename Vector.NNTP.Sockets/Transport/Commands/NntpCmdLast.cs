// <copyright file="NntpCmdLast.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: LAST command handler.

using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP LAST command.
    /// </summary>
    internal static class NntpCmdLast
    {
        /// <summary>
        /// Moves the current article pointer to the previous article in the selected group.
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

            long? previous = await storage.GetPreviousArticleNumberAsync(session.State.SelectedGroup, current, cancellationToken).ConfigureAwait(false);
            if (previous is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (previous.Value < 0)
            {
                await session.Writer.WriteLineAsync("422 No previous article in this group", cancellationToken).ConfigureAwait(false);
                return true;
            }

            session.State.CurrentArticleNumber = previous.Value;
            string? messageId = await storage.GetArticleMessageIdAsync(
                session.State.SelectedGroup,
                previous.Value,
                null,
                cancellationToken).ConfigureAwait(false);
            messageId ??= "<unknown@local>";

            await session.Writer.WriteLineAsync($"223 {previous.Value} {messageId}", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
