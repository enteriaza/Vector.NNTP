// <copyright file="NntpCmdSlave.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: SLAVE command stub.

using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Handles the legacy NNTP SLAVE command (not supported).
    /// </summary>
    internal static class NntpCmdSlave
    {
        /// <summary>
        /// Rejects SLAVE; this server does not implement slave-mode peering.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static ValueTask<bool> DispatchAsync(NntpSession session, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            return ContinueAfterWrite(NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken));
        }

        private static async ValueTask<bool> ContinueAfterWrite(ValueTask writeTask)
        {
            await writeTask.ConfigureAwait(false);
            return true;
        }
    }
}
