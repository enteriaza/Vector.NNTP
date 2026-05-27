// <copyright file="NntpCmdMode.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: MODE READER and MODE STREAM command handler.

using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles MODE READER and MODE STREAM commands.
    /// </summary>
    internal static class NntpCmdMode
    {
        /// <summary>
        /// Enables reader or streaming mode when permitted by the host profile.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(line);
            string arg = NntpCommandLineHelpers.GetArgument(line);
            if (arg.Equals("READER", StringComparison.OrdinalIgnoreCase))
            {
                if (!session.Profile.AdvertiseModeReader)
                {
                    await session.Writer.WriteLineAsync("502 MODE READER not permitted", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                session.State.Mode = NntpSessionMode.Reader;
                session.State.ReaderPipeliningEnabled = true;
                await session.Writer.WriteLineAsync(NntpPostingPolicy.FormatInitialGreeting(session), cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (arg.Equals("STREAM", StringComparison.OrdinalIgnoreCase))
            {
                if (!session.Profile.AdvertiseModeStream)
                {
                    await session.Writer.WriteLineAsync("502 MODE STREAM not permitted", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                session.State.Mode = NntpSessionMode.Stream;
                await session.Writer.WriteLineAsync("203 Streaming is OK", cancellationToken).ConfigureAwait(false);
                return true;
            }

            await session.Writer.WriteLineAsync("501 Unknown MODE argument", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
