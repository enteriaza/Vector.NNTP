// <copyright file="NntpCmdCheck.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: CHECK command handler (RFC 4644 streaming filter).

using Vector.NNTP.Sockets.Protocol;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP CHECK streaming filter command (RFC 4644).
    /// </summary>
    /// <remarks>
    /// CHECK is read-only: it probes HistoryDB without reserving the message-id. Peers use
    /// <c>238</c>/<c>438</c>/<c>431</c> responses to decide whether to pipeline TAKETHIS.
    /// </remarks>
    internal static class NntpCmdCheck
    {
        /// <summary>
        /// Evaluates whether an article is wanted without recording it in transit history.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="historyDatabase">Transit history for CHECK (may be null).</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            IHistoryDatabase? historyDatabase,
            string line,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(line);

            string? messageId = NntpCommandLineHelpers.ExtractArgument(line);
            if (string.IsNullOrEmpty(messageId) || !MessageIdSyntax.IsValid(messageId))
            {
                await session.Writer.WriteLineAsync("501 Message-ID required", cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (historyDatabase is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            HistoryCheckResult result = await historyDatabase.CheckAsync(messageId, cancellationToken)
                .ConfigureAwait(false);
            if (result == HistoryCheckResult.Unavailable)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            string responseLine = MapCheckResponse(result, messageId);
            await session.Writer.WriteLineAsync(responseLine, cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Maps a <see cref="HistoryCheckResult"/> to a CHECK response line.
        /// </summary>
        /// <param name="result">History probe result.</param>
        /// <param name="messageId">Message-id from the command line.</param>
        /// <returns>Single-line NNTP response without CRLF.</returns>
        /// <exception cref="NotImplementedException">
        /// Thrown when <paramref name="result"/> is <see cref="HistoryCheckResult.Unavailable"/>; callers must handle that case before mapping.
        /// </exception>
        private static string MapCheckResponse(HistoryCheckResult result, string messageId)
        {
            return result switch
            {
                HistoryCheckResult.Wanted => $"238 {messageId}",
                HistoryCheckResult.Duplicate => $"438 {messageId}",
                HistoryCheckResult.TryAgainLater => $"431 {messageId}",
                HistoryCheckResult.Unavailable => throw new NotImplementedException(),
                _ => "503 Service unavailable",
            };
        }
    }
}
