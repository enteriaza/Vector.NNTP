// <copyright file="ArticleSelectorSyntax.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// ArticleSelectorSyntax.cs -- ARTICLE/HEAD/BODY/STAT single-article selector parsing.

using System.Runtime.CompilerServices;

namespace Vector.NNTP.Sockets.Protocol
{
    /// <summary>
    /// Parses optional article selectors on reader retrieval commands (single article number or Message-ID).
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — delegates Message-ID validation to <see cref="ArticleRangeOrMessageIdSyntax"/> /
    /// <see cref="MessageIdSyntax"/>.</para>
    /// </remarks>
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryParse(string? argument, out long? articleNumber, out string? messageId)
        {
            return ArticleRangeOrMessageIdSyntax.TryParseSingleNumberOrMessageId(argument, out articleNumber, out messageId);
        }
    }
}
