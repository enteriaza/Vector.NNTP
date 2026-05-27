// <copyright file="NntpCmdDate.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: DATE command handler.

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Session;

    /// <summary>
    /// Handles the NNTP DATE command.
    /// </summary>
    internal static class NntpCmdDate
    {
        /// <summary>
        /// Sends the current UTC date and time in NNTP <c>111</c> format.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(NntpSession session, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            string yyyymmdd = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            await session.Writer.WriteLineAsync($"111 {yyyymmdd}", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
