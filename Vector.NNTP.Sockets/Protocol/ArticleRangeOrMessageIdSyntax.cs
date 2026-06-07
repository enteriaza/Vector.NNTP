// <copyright file="ArticleRangeOrMessageIdSyntax.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// ArticleRangeOrMessageIdSyntax.cs -- Shared article range or Message-ID selector parsing for OVER/HDR command arguments.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace Vector.NNTP.Sockets.Protocol
{
    /// <summary>
    /// Parses optional article number ranges or Message-ID selectors on reader commands that accept
    /// <c>[range | message-id]</c> arguments (OVER, XOVER, HDR, XHDR).
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — Message-ID branches delegate to <see cref="MessageIdSyntax"/> /
    /// <see cref="Vector.NNTP.Utilities.Validation.MessageIdValidation.IsValidMessageId(ReadOnlySpan{char}, bool)"/> without extra allocations on success.</para>
    /// <para>Malformed numeric ranges and invalid Message-IDs return <see langword="false"/> so handlers emit 501 instead of silently ignoring garbage.</para>
    /// </remarks>
    internal static class ArticleRangeOrMessageIdSyntax
    {
        /// <summary>
        /// Attempts to parse an argument into an article number range or Message-ID selector.
        /// </summary>
        /// <param name="argument">Argument text after the verb or header field name.</param>
        /// <param name="rangeLow">Inclusive low article number when a numeric selector is present.</param>
        /// <param name="rangeHigh">Inclusive high article number when a numeric selector is present.</param>
        /// <param name="messageId">Message-ID text when the argument is a valid Message-ID token.</param>
        /// <returns>
        /// <see langword="true"/> when the argument is empty, a numeric range/article number, or a valid Message-ID;
        /// <see langword="false"/> when the argument is present but syntactically invalid.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryParse(
            string? argument,
            out long? rangeLow,
            out long? rangeHigh,
            out string? messageId)
        {
            rangeLow = null;
            rangeHigh = null;
            messageId = null;

            if (string.IsNullOrWhiteSpace(argument))
            {
                return true;
            }

            if (TryParseNumericRange(argument, out rangeLow, out rangeHigh))
            {
                return true;
            }

            if (MessageIdSyntax.IsValid(argument))
            {
                messageId = argument;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to parse a single article number or Message-ID (no hyphenated ranges).
        /// </summary>
        /// <param name="argument">Argument text after the verb.</param>
        /// <param name="articleNumber">Parsed article number when numeric.</param>
        /// <param name="messageId">Parsed Message-ID when valid.</param>
        /// <returns><see langword="true"/> when empty, numeric, or a valid Message-ID; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryParseSingleNumberOrMessageId(
            string? argument,
            out long? articleNumber,
            out string? messageId)
        {
            articleNumber = null;
            messageId = null;

            if (string.IsNullOrWhiteSpace(argument))
            {
                return true;
            }

            if (argument.IndexOf('-', StringComparison.Ordinal) >= 0)
            {
                return false;
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

        /// <summary>
        /// Parses a numeric article number or inclusive <c>low-high</c> range from <paramref name="argument"/>.
        /// </summary>
        /// <param name="argument">Non-empty argument text.</param>
        /// <param name="rangeLow">Inclusive low bound on success.</param>
        /// <param name="rangeHigh">Inclusive high bound on success.</param>
        /// <returns><see langword="true"/> when the argument is a valid numeric selector.</returns>
        private static bool TryParseNumericRange(string argument, out long? rangeLow, out long? rangeHigh)
        {
            rangeLow = null;
            rangeHigh = null;

            int dash = argument.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                if (long.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out long single))
                {
                    rangeLow = single;
                    rangeHigh = single;
                    return true;
                }

                return false;
            }

            ReadOnlySpan<char> left = argument.AsSpan(0, dash).Trim();
            ReadOnlySpan<char> right = argument.AsSpan(dash + 1).Trim();
            if (left.IsEmpty || right.IsEmpty)
            {
                return false;
            }

            if (!long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out long low)
                || !long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out long high))
            {
                return false;
            }

            rangeLow = low;
            rangeHigh = high;
            return true;
        }
    }
}
