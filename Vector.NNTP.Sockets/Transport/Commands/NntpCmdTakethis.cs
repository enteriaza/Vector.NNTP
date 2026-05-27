// <copyright file="NntpCmdTakethis.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: TAKETHIS command handler (RFC 4644).

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Protocol;
    using Responses;
    using Session;
    using Storage;

    /// <summary>
    /// Handles the NNTP TAKETHIS streaming command.
    /// </summary>
    internal static class NntpCmdTakethis
    {
        /// <summary>
        /// Accepts a streaming-mode article transfer after CHECK filtering.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="storage">Transit storage (may be null).</param>
        /// <param name="line">Full command line.</param>
        /// <param name="lineReader">Line reader for the article body.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            INntpTransitStorage? storage,
            string line,
            Vector.NNTP.Sockets.Transport.NntpLineReader lineReader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(line);
            ArgumentNullException.ThrowIfNull(lineReader);
            if (storage is null)
            {
                await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                return true;
            }

            string? messageId = NntpCommandLineHelpers.ExtractArgument(line);
            if (string.IsNullOrEmpty(messageId) || !MessageIdSyntax.IsValid(messageId))
            {
                await session.Writer.WriteLineAsync("501 Message-ID required", cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteLineAsync("373 Send article", cancellationToken).ConfigureAwait(false);
            byte[] body = await Vector.NNTP.Sockets.Transport.NntpDotStuffingReader.ReadBodyAsync(lineReader, cancellationToken).ConfigureAwait(false);
            bool ok = await storage.TakeThisAsync(messageId, body, cancellationToken).ConfigureAwait(false);
            await session.Writer.WriteLineAsync(ok ? "235 Article transferred OK" : "439 Transfer failed", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
