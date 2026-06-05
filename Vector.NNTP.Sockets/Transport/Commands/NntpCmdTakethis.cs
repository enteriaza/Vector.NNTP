// <copyright file="NntpCmdTakethis.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: TAKETHIS command handler (RFC 4644).

using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.Sockets.Protocol;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP TAKETHIS streaming command (RFC 4644).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike IHAVE, TAKETHIS does not use an intermediate response: the article body follows the
    /// command line immediately (often pipelined after CHECK <c>238</c>). The handler records the
    /// message-id in HistoryDB before reading the body, then replies <c>239</c> or <c>439</c>.
    /// </para>
    /// </remarks>
    internal static class NntpCmdTakethis
    {
        /// <summary>
        /// Accepts a streaming-mode article transfer after CHECK filtering.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="historyDatabase">Transit history for record-before-body (may be null).</param>
        /// <param name="storage">Transit storage (may be null).</param>
        /// <param name="line">Full command line.</param>
        /// <param name="lineReader">Line reader for the article body.</param>
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
            if (historyDatabase is null || storage is null)
            {
                await DrainBodyAndRespondAsync(session, lineReader, "503 Service unavailable", cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            if (string.IsNullOrEmpty(messageId) || !MessageIdSyntax.IsValid(messageId))
            {
                await DrainBodyAndRespondAsync(session, lineReader, "501 Message-ID required", cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            HistoryRecordResult record = await historyDatabase.TryRecordAsync(messageId, cancellationToken)
                .ConfigureAwait(false);
            if (record == HistoryRecordResult.Unavailable)
            {
                await DrainBodyAndRespondAsync(session, lineReader, "503 Service unavailable", cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            if (record == HistoryRecordResult.Duplicate)
            {
                await DrainBodyAndRespondAsync(session, lineReader, "439 Transfer failed", cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            if (record == HistoryRecordResult.TryAgainLater)
            {
                await DrainBodyAndRespondAsync(session, lineReader, "439 Transfer failed", cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

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
                    ok ? "239 Article transferred OK" : "439 Transfer failed",
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                session.State.MultiLineBodyPending = false;
            }
        }

        /// <summary>
        /// Reads a pipelined article body and writes a single-line response.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="lineReader">Line reader.</param>
        /// <param name="responseLine">Response line without CRLF.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private static async Task DrainBodyAndRespondAsync(
            NntpSession session,
            NntpLineReader lineReader,
            string responseLine,
            CancellationToken cancellationToken)
        {
            session.State.MultiLineBodyPending = true;
            try
            {
                await NntpDotStuffingReader.DrainBodyAsync(lineReader, cancellationToken).ConfigureAwait(false);
                await session.Writer.WriteLineAsync(responseLine, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                session.State.MultiLineBodyPending = false;
            }
        }
    }
}
