// <copyright file="NntpCmdAuthinfo.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: AUTHINFO command handler wrapper.

namespace Vector.NNTP.Sockets.Transport.Commands
{
    using Authentication;
    using Session;

    /// <summary>
    /// Thin wrapper delegating AUTHINFO handling to <see cref="NntpAuthenticationService"/>.
    /// </summary>
    internal static class NntpCmdAuthinfo
    {
        /// <summary>
        /// Handles an AUTHINFO command line via the injected authentication service.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="auth">Authentication service.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> to continue the session.</returns>
        internal static async ValueTask<bool> DispatchAsync(
            NntpSession session,
            NntpAuthenticationService auth,
            string line,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(line);
            await auth.HandleAuthInfoAsync(session, line, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
