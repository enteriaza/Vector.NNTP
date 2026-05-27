// <copyright file="NntpCmdGroup.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: GROUP command handler.

using Vector.NNTP.Sockets.Protocol;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP GROUP command.
    /// </summary>
    internal static class NntpCmdGroup
    {
        /// <summary>
        /// Selects a newsgroup and updates session article range state.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="storage">Article storage (may be null).</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            INntpArticleStorage? storage,
            string line,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(line);
            if (storage is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            string? groupName = NntpCommandLineHelpers.ExtractArgument(line);
            if (string.IsNullOrEmpty(groupName))
            {
                await session.Writer.WriteLineAsync("501 GROUP requires newsgroup name", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (!NewsgroupNameSyntax.IsValid(groupName))
            {
                await NntpReaderErrors.WriteBadSyntax501(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            NntpGroupInfo? info = await storage.SelectGroupAsync(groupName, cancellationToken).ConfigureAwait(false);
            if (info is null)
            {
                await NntpReaderErrors.WriteNoSuchGroup411(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            session.State.SelectedGroup = info.Name;
            session.State.SelectedGroupLowWater = info.LowWater;
            session.State.SelectedGroupHighWater = info.HighWater;
            session.State.SelectedGroupEstimatedCount = info.ArticleCount;
            session.State.CurrentArticleNumber = info.HighWater;
            await session.Writer.WriteLineAsync(
                $"211 {info.ArticleCount} {info.LowWater} {info.HighWater} {info.Name} selected",
                cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
