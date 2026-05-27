// <copyright file="NntpSessionGreeting.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: initial 200/201 greeting after connection accept.

namespace Vector.NNTP.Sockets.Transport
{
    using Session;

    /// <summary>
    /// Sends the RFC 3977 service-ready greeting for a new NNTP session.
    /// </summary>
    internal static class NntpSessionGreeting
    {
        /// <summary>
        /// Sends the initial <c>200</c> or <c>201</c> greeting after accept.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the greeting is sent.</returns>
        internal static ValueTask SendAsync(NntpSession session, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            string line = NntpPostingPolicy.FormatInitialGreeting(session);
            return session.Writer.WriteLineAsync(line, cancellationToken);
        }
    }
}
