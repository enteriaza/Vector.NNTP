// <copyright file="NntpHelpContent.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: HELP multi-line bodies per host profile (informative; not a substitute for CAPABILITIES).

using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Supplies RFC 3977 <c>HELP</c> command bodies aligned with legacy NNRPD and NNTPD deployments.
    /// </summary>
    internal static class NntpHelpContent
    {
        /// <summary>
        /// Gets HELP body lines (without the <c>100</c> header or terminating dot) for <paramref name="session"/>.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <returns>Help text lines.</returns>
        internal static string[] GetLines(NntpSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            return session.Profile.Role == NntpHostRole.Transit
                ? TransitHelpLines
                : ReaderHelpLines;
        }

        /// <summary>
        /// Gets the help text lines for a transit host.
        /// </summary>
        /// <returns>The help text lines.</returns>
        private static readonly string[] TransitHelpLines =
        [
            "  QUIT",
            "  CAPABILITIES [keyword]",
            "  MODE STREAM",
            "  HELP",
            "  DATE",
            "  AUTHINFO USER <username>",
            "  AUTHINFO PASS <password>",
            "  STARTTLS",
            "  COMPRESS DEFLATE",
            "  CHECK <message-id>",
            "  IHAVE <message-id>",
            "  TAKETHIS <message-id>",
        ];

        /// <summary>
        /// Gets the help text lines for a reader host.
        /// </summary>
        /// <returns>The help text lines.</returns>
        private static readonly string[] ReaderHelpLines =
        [
            "  QUIT",
            "  CAPABILITIES [keyword]",
            "  MODE READER",
            "  HELP",
            "  DATE",
            "  AUTHINFO USER <username>",
            "  AUTHINFO PASS <password>",
            "  STARTTLS",
            "  COMPRESS DEFLATE",
            "  GROUP <name>",
            "  LISTGROUP [name [range]]",
            "  NEXT",
            "  LAST",
            "  ARTICLE [<message-id> | number]",
            "  HEAD [<message-id> | number]",
            "  BODY [<message-id> | number]",
            "  STAT [<message-id> | number]",
            "  OVER [range | <message-id>]",
            "  HDR <field> [range | <message-id>]",
            "  LIST [keyword [wildmat]]",
            "  POST",
            "  XOVER [range]",
            "  XHDR <field> [range | <message-id>]",
            "  NEWGROUPS (recognised, 503)",
            "  NEWNEWS (recognised, 503)",
            "  XPAT (recognised, 503)",
        ];
    }
}
