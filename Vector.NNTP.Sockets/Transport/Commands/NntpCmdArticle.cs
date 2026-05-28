// <copyright file="NntpCmdArticle.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: ARTICLE, HEAD, BODY, and STAT command handlers.

using Vector.NNTP.Sockets.Protocol;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles ARTICLE, HEAD, BODY, and STAT retrieval commands.
    /// </summary>
    internal static class NntpCmdArticle
    {
        /// <summary>
        /// Retrieves an article or part and streams the multi-line body when required.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="storage">Article storage (may be null).</param>
        /// <param name="verb">ARTICLE, HEAD, BODY, or STAT.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            INntpArticleStorage? storage,
            string verb,
            string line,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(verb);
            ArgumentNullException.ThrowIfNull(line);
            if (storage is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            NntpArticlePart part = verb.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ? NntpArticlePart.Head
                : verb.Equals("BODY", StringComparison.OrdinalIgnoreCase) ? NntpArticlePart.Body
                : verb.Equals("STAT", StringComparison.OrdinalIgnoreCase) ? NntpArticlePart.Stat
                : NntpArticlePart.Full;

            string? arg = NntpCommandLineHelpers.ExtractArgument(line);
            if (!ArticleSelectorSyntax.TryParse(arg, out long? articleNumber, out string? messageId))
            {
                await NntpReaderErrors.WriteBadSyntax501(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (messageId is null && string.IsNullOrEmpty(session.State.SelectedGroup))
            {
                await NntpReaderErrors.WriteNoGroupSelected412(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (articleNumber is null && string.IsNullOrEmpty(arg))
            {
                articleNumber = session.State.CurrentArticleNumber;
            }

            if (articleNumber is not null && messageId is null && IsOutOfRange(session, articleNumber.Value))
            {
                await NntpReaderErrors.WriteArticleOutOfRange423(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            NntpArticlePayload? payload = await storage.GetArticleAsync(
                session.State.SelectedGroup,
                articleNumber,
                messageId,
                part,
                cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                await NntpReaderErrors.WriteNoSuchArticle430(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            session.State.CurrentArticleNumber = payload.ArticleNumber;
            string? resolvedMessageId = await storage.GetArticleMessageIdAsync(
                session.State.SelectedGroup,
                payload.ArticleNumber,
                messageId,
                cancellationToken).ConfigureAwait(false);
            resolvedMessageId ??= "<unknown@local>";

            if (part == NntpArticlePart.Stat)
            {
                await session.Writer.WriteLineAsync(
                    $"223 {payload.ArticleNumber} {resolvedMessageId}",
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteLineAsync(
                $"220 {payload.ArticleNumber} {resolvedMessageId} article",
                cancellationToken).ConfigureAwait(false);
            await session.Writer.WriteDotStuffedArticleBodyAsync(payload.Body, cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Checks if an article number is out of range.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="articleNumber">The article number.</param>
        /// <returns>True if the article number is out of range, false otherwise.</returns>
        private static bool IsOutOfRange(NntpSession session, long articleNumber)
        {
            return (session.State.SelectedGroupLowWater is long low && articleNumber < low) || (session.State.SelectedGroupHighWater is long high && articleNumber > high);
        }
    }
}
