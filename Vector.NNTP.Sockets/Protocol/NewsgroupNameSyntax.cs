// <copyright file="NewsgroupNameSyntax.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC 3977 newsgroup name syntax validation.

namespace Vector.NNTP.Sockets.Protocol
{
    /// <summary>
    /// Validates newsgroup names for GROUP and related reader commands.
    /// </summary>
    internal static class NewsgroupNameSyntax
    {
        /// <summary>
        /// Determines whether <paramref name="name"/> satisfies basic RFC 3977 newsgroup name rules.
        /// </summary>
        /// <param name="name">Candidate newsgroup name.</param>
        /// <returns>
        /// <see langword="true"/> when the name is non-empty, contains no whitespace, and includes at least one dot.
        /// </returns>
        internal static bool IsValid(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && !name.AsSpan().ContainsAny(" \t\r\n") && name.Contains('.', StringComparison.Ordinal);
        }
    }
}
