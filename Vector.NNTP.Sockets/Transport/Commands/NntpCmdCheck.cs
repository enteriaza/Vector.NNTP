// <copyright file="NntpCmdCheck.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: CHECK and IHAVE command handlers (RFC 4644).

using Vector.NNTP.Sockets.Protocol;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles CHECK and IHAVE transit commands.
    /// </summary>
    internal static class NntpCmdCheck
    {
        /// <summary>
        /// Handles CHECK (streaming filter) or IHAVE (offer and optional body transfer).
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="historyDatabase">Transit history for CHECK (may be null).</param>
        /// <param name="storage">Transit storage for IHAVE/TAKETHIS (may be null).</param>
        /// <param name="verb">CHECK or IHAVE.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="lineReader">Line reader for IHAVE article body.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            IHistoryDatabase? historyDatabase,
            INntpTransitStorage? storage,
            string verb,
            string line,
            NntpLineReader lineReader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(verb);
            ArgumentNullException.ThrowIfNull(line);
            ArgumentNullException.ThrowIfNull(lineReader);

            string? messageId = NntpCommandLineHelpers.ExtractArgument(line);
            if (string.IsNullOrEmpty(messageId) || !MessageIdSyntax.IsValid(messageId))
            {
                await session.Writer.WriteLineAsync("501 Message-ID required", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (verb.Equals("CHECK", StringComparison.OrdinalIgnoreCase))
            {
                if (historyDatabase is null)
                {
                    await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                HistoryCheckResult result = await historyDatabase.CheckAsync(messageId, cancellationToken)
                    .ConfigureAwait(false);
                string responseLine = MapCheckResponse(result, messageId);
                if (result == HistoryCheckResult.Unavailable)
                {
                    await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                await session.Writer.WriteLineAsync(responseLine, cancellationToken).ConfigureAwait(false);
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

        private static string MapCheckResponse(HistoryCheckResult result, string messageId) =>
            result switch
            {
                HistoryCheckResult.Wanted => $"238 {messageId}",
                HistoryCheckResult.Duplicate => $"438 {messageId}",
                HistoryCheckResult.TryAgainLater => $"431 {messageId}",
                _ => "503 Service unavailable",
            };
    }
}
