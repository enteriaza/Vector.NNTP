// <copyright file="NntpCmdIHave.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: IHAVE command handler (RFC 4644).

using Vector.NNTP.Sockets.Protocol;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP IHAVE offer-and-transfer command (RFC 4644).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike TAKETHIS, IHAVE uses a two-step exchange: the server replies <c>335 Send article</c>
    /// before the dot-stuffed body is read. HistoryDB <c>TryRecordAsync</c> runs before the body
    /// transfer; transit storage persists the article and the handler replies <c>235</c> or <c>439</c>.
    /// </para>
    /// </remarks>
    internal static class NntpCmdIHave
    {
        /// <summary>
        /// Accepts an IHAVE offer, records the message-id, and transfers the article body.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="historyDatabase">Transit history for record-before-body (may be null).</param>
        /// <param name="storage">Transit storage for the article body (may be null).</param>
        /// <param name="line">Full command line.</param>
        /// <param name="lineReader">Line reader for the article body after <c>335</c>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            IHistoryDatabase? historyDatabase,
            INntpTransitStorage? storage,
            string line,
            NntpLineReader lineReader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(line);
            ArgumentNullException.ThrowIfNull(lineReader);

            string? messageId = NntpCommandLineHelpers.ExtractArgument(line);
            if (string.IsNullOrEmpty(messageId) || !MessageIdSyntax.IsValid(messageId))
            {
                await session.Writer.WriteLineAsync("501 Message-ID required", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (historyDatabase is null || storage is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            HistoryRecordResult record = await historyDatabase.TryRecordAsync(messageId, cancellationToken)
                .ConfigureAwait(false);
            if (record == HistoryRecordResult.Unavailable)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (record == HistoryRecordResult.Duplicate)
            {
                await session.Writer.WriteLineAsync("435 Already have it", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (record == HistoryRecordResult.TryAgainLater)
            {
                await session.Writer.WriteLineAsync("431 Try again later", cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteLineAsync("335 Send article", cancellationToken).ConfigureAwait(false);
            session.State.MultiLineBodyPending = true;
            try
            {
                NntpArticleBodyReadResult read = await NntpDotStuffingReader.ReadBodyAsync(
                    lineReader,
                    session.Options.MaxArtSize,
                    cancellationToken).ConfigureAwait(false);
                if (read.Status == NntpArticleBodyReadStatus.ExceededMaxSize)
                {
                    await NntpDotStuffingReader.DrainBodyAsync(lineReader, cancellationToken).ConfigureAwait(false);
                    await session.Writer.WriteLineAsync("439 Transfer failed", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                bool ok = await storage.TakeThisAsync(messageId, read.Body, cancellationToken).ConfigureAwait(false);
                await session.Writer.WriteLineAsync(
                    ok ? "235 Article transferred OK" : "439 Transfer failed",
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                session.State.MultiLineBodyPending = false;
            }
        }
    }
}
