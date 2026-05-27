// <copyright file="ArticleSelectorSyntax.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: ARTICLE/HEAD/BODY/STAT selector parsing.

namespace Vector.NNTP.Sockets.Protocol
{
    /// <summary>
    /// Parses optional article selectors on reader retrieval commands.
    /// </summary>
    internal static class ArticleSelectorSyntax
    {
        /// <summary>
        /// Attempts to parse an article selector argument into a number or Message-ID.
        /// </summary>
        /// <param name="argument">Argument text after the verb (may be null or empty for current article).</param>
        /// <param name="articleNumber">Parsed article number when the argument is numeric.</param>
        /// <param name="messageId">Parsed Message-ID when the argument uses angle brackets.</param>
        /// <returns>
        /// <see langword="true"/> when the argument is empty, numeric, or a valid Message-ID;
        /// <see langword="false"/> when the argument is present but syntactically invalid.
        /// </returns>
        internal static bool TryParse(string? argument, out long? articleNumber, out string? messageId)
        {
            articleNumber = null;
            messageId = null;

            if (string.IsNullOrWhiteSpace(argument))
            {
                return true;
            }

            if (long.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
            {
                articleNumber = number;
                return true;
            }

            if (MessageIdSyntax.IsValid(argument))
            {
                messageId = argument;
                return true;
            }

            return false;
        }
    }
}
