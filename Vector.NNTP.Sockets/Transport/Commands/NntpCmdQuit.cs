// <copyright file="NntpCmdQuit.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: QUIT command handler.

using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP QUIT command.
    /// </summary>
    internal static class NntpCmdQuit
    {
        /// <summary>
        /// Sends <c>205 Connection closing</c> and marks the session for teardown.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="false"/> to end the session loop.</returns>
        internal static async ValueTask<bool> DispatchAsync(NntpSession session, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            session.State.QuitRequested = true;
            await session.Writer.WriteLineAsync("205 Connection closing", cancellationToken).ConfigureAwait(false);
            return false;
        }
    }
}
