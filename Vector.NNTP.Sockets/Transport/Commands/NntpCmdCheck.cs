// <copyright file="NntpCmdCheck.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: CHECK and IHAVE command handlers (RFC 4644).

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Protocol;
    using Responses;
    using Session;
    using Storage;

    /// <summary>
    /// Handles CHECK and IHAVE transit commands.
    /// </summary>
    internal static class NntpCmdCheck
    {
        /// <summary>
        /// Handles CHECK (streaming filter) or IHAVE (offer and optional body transfer).
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="storage">Transit storage (may be null).</param>
        /// <param name="verb">CHECK or IHAVE.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="lineReader">Line reader for IHAVE article body.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            INntpTransitStorage? storage,
            string verb,
            string line,
            Vector.NNTP.Sockets.Transport.NntpLineReader lineReader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(verb);
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

            if (verb.Equals("CHECK", StringComparison.OrdinalIgnoreCase))
            {
                bool wanted = await storage.CheckAsync(messageId, cancellationToken).ConfigureAwait(false);
                await session.Writer.WriteLineAsync(
                    wanted ? "238 Article wanted" : "438 Article not wanted",
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            bool sendBody = await storage.IHaveAsync(messageId, cancellationToken).ConfigureAwait(false);
            if (!sendBody)
            {
                await session.Writer.WriteLineAsync("435 Already have it", cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteLineAsync("335 Send article", cancellationToken).ConfigureAwait(false);
            byte[] body = await Vector.NNTP.Sockets.Transport.NntpDotStuffingReader.ReadBodyAsync(lineReader, cancellationToken).ConfigureAwait(false);
            bool ok = await storage.TakeThisAsync(messageId, body, cancellationToken).ConfigureAwait(false);
            await session.Writer.WriteLineAsync(ok ? "235 Article transferred OK" : "439 Transfer failed", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
