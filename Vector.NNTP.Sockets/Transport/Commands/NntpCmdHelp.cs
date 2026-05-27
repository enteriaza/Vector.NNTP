// <copyright file="NntpCmdHelp.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: HELP command handler.

using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the NNTP HELP command.
    /// </summary>
    internal static class NntpCmdHelp
    {
        /// <summary>
        /// Sends the profile-specific multi-line help listing.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static ValueTask<bool> DispatchAsync(NntpSession session, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            string[] lines = NntpHelpContent.GetLines(session);
            return ContinueAfterWrite(session.Writer.WriteMultiLineAsync("100 Help text follows", lines, cancellationToken));
        }

        private static async ValueTask<bool> ContinueAfterWrite(ValueTask writeTask)
        {
            await writeTask.ConfigureAwait(false);
            return true;
        }
    }
}
