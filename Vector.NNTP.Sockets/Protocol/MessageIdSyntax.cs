// <copyright file="MessageIdSyntax.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.

using System.Runtime.CompilerServices;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Sockets.Protocol
{
    /// <summary>
    /// Validates Message-ID tokens used by transit and reader commands.
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — delegates to <see cref="MessageIdValidation.IsValidMessageId(ReadOnlySpan{char}, bool)"/> without intermediate strings.</para>
    /// <para>INN <c>messageid.c</c> rules; used on transit and reader command argument lines.</para>
    /// </remarks>
    internal static class MessageIdSyntax
    {
        /// <summary>
        /// Determines whether <paramref name="messageId"/> is a syntactically valid Message-ID token.
        /// </summary>
        /// <param name="messageId">Candidate Message-ID (typically including angle brackets).</param>
        /// <returns><see langword="true"/> when the token satisfies RFC 3977 and RFC 5536 atom grammar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValid(ReadOnlySpan<char> messageId)
        {
            return MessageIdValidation.IsValidMessageId(messageId);
        }

        /// <summary>
        /// Determines whether <paramref name="messageId"/> is a syntactically valid Message-ID token.
        /// </summary>
        /// <param name="messageId">Candidate Message-ID string.</param>
        /// <returns><see langword="true"/> when the token satisfies RFC 3977 and RFC 5536 atom grammar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValid(string messageId)
        {
            return MessageIdValidation.IsValidMessageId(messageId);
        }
    }
}
