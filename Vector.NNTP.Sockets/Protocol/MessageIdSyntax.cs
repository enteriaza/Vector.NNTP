// <copyright file="MessageIdSyntax.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC 5322 Message-ID angle-bracket validation for NNTP commands.

namespace Vector.NNTP.Sockets.Protocol
{
    /// <summary>
    /// Validates Message-ID tokens used by transit and reader commands.
    /// </summary>
    internal static class MessageIdSyntax
    {
        /// <summary>
        /// Determines whether <paramref name="messageId"/> is a syntactically valid Message-ID token.
        /// </summary>
        /// <param name="messageId">Candidate Message-ID (typically including angle brackets).</param>
        /// <returns><see langword="true"/> when the token is non-empty and wrapped in angle brackets.</returns>
        internal static bool IsValid(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return false;
            }

            return messageId.Length >= 3
                && messageId[0] == '<'
                && messageId[^1] == '>';
        }
    }
}
