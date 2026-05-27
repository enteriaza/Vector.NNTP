// <copyright file="NntpCmdNewgroups.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: NEWGROUPS command stub.

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Responses;
    using Session;

    /// <summary>
    /// Handles the NNTP NEWGROUPS command (not yet implemented).
    /// </summary>
    internal static class NntpCmdNewgroups
    {
        /// <summary>
        /// Rejects NEWGROUPS until distributed storage provides newgroups queries.
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
