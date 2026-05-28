// <copyright file="NntpPostingPolicy.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: posting permission and RFC 3977 greeting text shared by greeting, MODE, POST, and CAPABILITIES.

using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Reader posting policy helpers shared by greeting, <c>MODE</c>, <c>POST</c>, and <c>CAPABILITIES</c>.
    /// </summary>
    internal static class NntpPostingPolicy
    {
        /// <summary>
        /// Builds the initial service-ready greeting (RFC 3977 §5.1.1) for the session role and authentication state.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <returns>Single-line greeting including CRLF is not appended; caller writes via <see cref="Responses.NntpResponseWriter"/>.</returns>
        internal static string FormatInitialGreeting(NntpSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            bool postingPermitted = IsPostingPermitted(session);
            return FormatServiceReadyLine(session.Options.ServerIdentification, postingPermitted, session.Profile.Role);
        }

        /// <summary>
        /// Determines whether POST and posting-allowed greetings apply for the current session.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <returns><see langword="true"/> when posting is permitted.</returns>
        internal static bool IsPostingPermitted(NntpSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            INntpHostProfile host = session.Profile;
            NntpConnectionContext connection = session.Connection;

            return host.Role == NntpHostRole.Reader && host.AllowsReaderCommands
                ? connection.IsAuthenticated && (connection.Policy?.AllowPosting ?? false)
                : host.Role == NntpHostRole.Transit;
        }

        /// <summary>
        /// Formats the service-ready line for the session role and authentication state.
        /// </summary>
        /// <param name="serverIdentification">Server identification.</param>
        /// <param name="postingPermitted">Whether posting is permitted.</param>
        /// <param name="role">Session role.</param>
        /// <returns>The service-ready line.</returns>
        private static string FormatServiceReadyLine(string serverIdentification, bool postingPermitted, NntpHostRole role)
        {
            return role == NntpHostRole.Transit
                ? postingPermitted
                    ? $"200 {serverIdentification} ready - posting allowed"
                    : $"201 {serverIdentification} ready - posting prohibited"
                : postingPermitted
                ? $"200 {serverIdentification} Service Ready, posting allowed (yEnc validation enabled)"
                : $"201 {serverIdentification} Service Ready, posting not allowed (yEnc validation enabled)";
        }
    }
}
