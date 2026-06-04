// <copyright file="NntpCmdPost.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: POST command handler.

using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP POST command.
    /// </summary>
    internal static class NntpCmdPost
    {
        /// <summary>
        /// Accepts a dot-stuffed article body and stores it via article storage.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="storage">Article storage (may be null).</param>
        /// <param name="lineReader">Line reader for the multi-line body.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            INntpArticleStorage? storage,
            NntpLineReader lineReader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(lineReader);

            bool canPost = storage is not null && session.Connection.Policy?.AllowPosting == true;

            // Always expect and read the body due to pipelining
            if (canPost)
            {
                await session.Writer.WriteLineAsync("340 Send article to be posted", cancellationToken).ConfigureAwait(false);
            }

            session.State.MultiLineBodyPending = true;
            try
            {
                byte[] body = await NntpDotStuffingReader.ReadBodyAsync(lineReader, cancellationToken).ConfigureAwait(false);

                // Now handle the rejection cases if validation failed
                if (storage is null)
                {
                    await session.Writer.WriteLineAsync("503 Reader storage not configured", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (session.Connection.Policy?.AllowPosting != true)
                {
                    await session.Writer.WriteLineAsync("480 Posting not permitted", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                NntpPostResult result = await storage.PostArticleAsync(body, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    await session.Writer.WriteLineAsync("441 Posting failed", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                await session.Writer.WriteLineAsync($"240 Article posted OK <{result.MessageId}>", cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                session.State.MultiLineBodyPending = false;
            }
        }
    }
}
