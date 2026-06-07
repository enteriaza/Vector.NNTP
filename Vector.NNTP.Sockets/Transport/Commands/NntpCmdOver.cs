// <copyright file="NntpCmdOver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: OVER and XOVER command handler.

using Vector.NNTP.Sockets.Protocol;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles OVER and XOVER overview retrieval commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a syntactically valid Message-ID selector is supplied, overview lookup by message-id is not yet implemented;
    /// the handler falls back to the default group range until storage grows message-id overview support.
    /// </para>
    /// </remarks>
    internal static class NntpCmdOver
    {
        /// <summary>
        /// Returns overview database lines for a range of articles in the selected group.
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

            if (!ArticleRangeOrMessageIdSyntax.TryParse(
                NntpCommandLineHelpers.ExtractArgument(line),
                out long? rangeLow,
                out long? rangeHigh,
                out string? _))
            {
                await NntpReaderErrors.WriteBadSyntax501(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            IReadOnlyList<string>? lines = await storage.GetOverviewAsync(
                session.State.SelectedGroup,
                rangeLow,
                rangeHigh,
                cancellationToken).ConfigureAwait(false);
            if (lines is null)
            {
                await session.Writer.WriteMultiLineAsync("224 Overview data follow", [], cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteMultiLineAsync("224 Overview data follow", lines, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
