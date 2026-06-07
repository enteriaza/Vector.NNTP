// <copyright file="MessageIdValidation.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.

using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Validation
{
    /// <summary>
    /// Validates NNTP Message-ID tokens per RFC 3977 length and printable US-ASCII, using INN
    /// <c>messageid.c</c> dot-atom grammar (RFC 5536 mdtext without <c>laxsyntax</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH — zero heap allocations on success; index-based parsing avoids sub-spans;
    /// bitmap character classes and Vector128/Vector256 ASCII and alphanumeric scans accelerate transit (CHECK/IHAVE/TAKETHIS)
    /// and reader (ARTICLE/BODY/HEAD/STAT) command argument validation.</para>
    /// <para>
    /// <b>Quoted local-parts are intentionally rejected.</b> RFC 5322 allows <c>&lt;"foo"@example.com&gt;</c>, but INN
    /// <c>IsValidMessageID</c> parses only dot-atom-text on the left-hand side (no <c>quoted-string</c>). This validator
    /// matches that INN behavior — stricter than the full RFC 5322 Message-ID grammar, but aligned with common NNTP
    /// transit checks.
    /// </para>
    /// <para>
    /// Domain literals (<c>[127.0.0.1]</c>) are accepted only as the first (and only) domain component, matching INN.
    /// Hybrid forms such as <c>foo.[127.0.0.1]</c> are rejected.
    /// </para>
    /// <para>
    /// <b>Public API:</b> Only <see cref="IsValidMessageId(ReadOnlySpan{char}, bool)"/> and
    /// <see cref="IsValidMessageId(string?, bool)"/> are public. Domain-only helpers, length constants, and SIMD/bitmap
    /// implementation types are <see langword="internal"/> to this assembly.
    /// </para>
    /// </remarks>
    public static class MessageIdValidation
    {
        /// <summary>
        /// Maximum Message-ID length in octets per RFC 3977.
        /// </summary>
        internal const int MaxMessageIdLength = 250;

        /// <summary>
        /// Minimum Message-ID length in octets per RFC 3977.
        /// </summary>
        internal const int MinMessageIdLength = 3;

        /// <summary>
        /// Determines whether <paramref name="messageId"/> is a syntactically valid Message-ID.
        /// </summary>
        /// <param name="messageId">Candidate Message-ID (typically including angle brackets).</param>
        /// <param name="stripSpaces">When <see langword="true"/>, leading and trailing whitespace is discarded before validation.</param>
        /// <returns><see langword="true"/> when the token satisfies RFC 3977 and RFC 5536 atom grammar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidMessageId(ReadOnlySpan<char> messageId, bool stripSpaces = false)
        {
            int length = messageId.Length;
            if (length is 0 or > MaxMessageIdLength)
            {
                return false;
            }

            int start = 0;
            int end = length;
            if (stripSpaces)
            {
                start = MessageIdValidationSimd.TrimLeadingWhitespace(messageId, start, end);
                end = MessageIdValidationSimd.TrimTrailingWhitespace(messageId, start, end);
            }

            if (end - start < MinMessageIdLength)
            {
                return false;
            }

            if (!MessageIdValidationSimd.IsAllAscii(messageId, start, end))
            {
                return false;
            }

            if (messageId[start] != '<')
            {
                return false;
            }

            if (!TryParseDotAtomSequence(messageId, startIndex: start + 1, endIndex: end, stopChar: '@', out int atIndex))
            {
                return false;
            }

            int domainStart = atIndex + 1;
            int closeIndex = end - 1;
            return domainStart < closeIndex
                && messageId[closeIndex] == '>'
                && IsValidRightPartMessageId(messageId, domainStart, closeIndex, stripSpaces: false, bracket: false);
        }

        /// <summary>
        /// Determines whether <paramref name="messageId"/> is a syntactically valid Message-ID.
        /// </summary>
        /// <param name="messageId">Candidate Message-ID string.</param>
        /// <param name="stripSpaces">When <see langword="true"/>, leading and trailing whitespace is discarded before validation.</param>
        /// <returns><see langword="true"/> when the token satisfies RFC 3977 and RFC 5536 atom grammar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidMessageId(string? messageId, bool stripSpaces = false)
        {
            return messageId is { Length: > 0 } && IsValidMessageId(messageId.AsSpan(), stripSpaces);
        }

        /// <summary>
        /// Determines whether <paramref name="domain"/> is a valid domain token (right-hand side of a Message-ID).
        /// </summary>
        /// <param name="domain">Domain text without angle brackets.</param>
        /// <returns><see langword="true"/> when the domain satisfies Message-ID domain grammar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValidDomain(ReadOnlySpan<char> domain)
        {
            return !domain.IsEmpty
                && MessageIdValidationSimd.IsAllAscii(domain, start: 0, end: domain.Length)
                && IsValidRightPartMessageId(domain, startIndex: 0, endIndex: domain.Length, stripSpaces: false, bracket: false);
        }

        /// <summary>
        /// Determines whether <paramref name="domain"/> is a valid domain token (right-hand side of a Message-ID).
        /// </summary>
        /// <param name="domain">Domain text without angle brackets.</param>
        /// <returns><see langword="true"/> when the domain satisfies Message-ID domain grammar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValidDomain(string? domain)
        {
            return domain is { Length: > 0 } && IsValidDomain(domain.AsSpan());
        }

        /// <summary>
        /// Validates the right-hand side of a Message-ID (domain part) within a bounded index range.
        /// </summary>
        /// <param name="span">Source span containing the domain text.</param>
        /// <param name="startIndex">Inclusive start index of the domain (first character after <c>@</c> when embedded).</param>
        /// <param name="endIndex">Exclusive end index (before closing <c>&gt;</c> when embedded).</param>
        /// <param name="stripSpaces">When <see langword="true"/>, trailing whitespace is discarded.</param>
        /// <param name="bracket">When <see langword="true"/>, a closing <c>&gt;</c> is required at <paramref name="endIndex"/>.</param>
        /// <returns><see langword="true"/> when domain syntax is valid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidRightPartMessageId(
            ReadOnlySpan<char> span,
            int startIndex,
            int endIndex,
            bool stripSpaces,
            bool bracket)
        {
            if (startIndex >= endIndex)
            {
                return false;
            }

            int index;
            if (span[startIndex] == '[')
            {
                if (!TryParseDomainLiteral(span, startIndex, endIndex, out index))
                {
                    return false;
                }
            }
            else if (!TryParseDotAtomSequence(span, startIndex, endIndex, stopChar: '\0', out index))
            {
                return false;
            }

            if (bracket)
            {
                if (index >= endIndex || span[index] != '>')
                {
                    return false;
                }

                index++;
            }

            if (stripSpaces)
            {
                index = MessageIdValidationSimd.TrimLeadingWhitespace(span, index, endIndex);
            }

            return index == endIndex;
        }

        /// <summary>
        /// Parses a dot-atom-text sequence until <paramref name="stopChar"/> or end of span.
        /// </summary>
        /// <param name="span">Input span (local-part or domain without brackets).</param>
        /// <param name="startIndex">Index of the first character after <c>&lt;</c> or domain start.</param>
        /// <param name="endIndex">Exclusive end of the parse range.</param>
        /// <param name="stopChar">Terminator character (<c>@</c> for local-part; <c>\0</c> for domain-only parse).</param>
        /// <param name="stopIndex">Index of the terminator or end position on success.</param>
        /// <returns>
        /// <see langword="true"/> when at least one atom was parsed and empty components (trailing dots, <c>..</c>) were not present.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseDotAtomSequence(
            ReadOnlySpan<char> span,
            int startIndex,
            int endIndex,
            char stopChar,
            out int stopIndex)
        {
            stopIndex = startIndex;
            if (startIndex >= endIndex)
            {
                return false;
            }

            bool parsedAtom = false;
            int index = startIndex;
            while (index < endIndex)
            {
                int consumed = MessageIdValidationSimd.ConsumeAtomCharacters(span, index, endIndex);
                if (consumed == 0)
                {
                    return false;
                }

                parsedAtom = true;
                index += consumed;

                if (index >= endIndex)
                {
                    stopIndex = index;
                    return parsedAtom && stopChar == '\0';
                }

                if (stopChar != '\0' && span[index] == stopChar)
                {
                    stopIndex = index;
                    return parsedAtom;
                }

                if (span[index] != '.')
                {
                    return false;
                }

                index++;
                if (index >= endIndex)
                {
                    return false;
                }
            }

            stopIndex = index;
            return parsedAtom && stopChar == '\0';
        }

        /// <summary>
        /// Parses a domain no-fold-literal (<c>[...]</c>) that must be the entire domain component.
        /// </summary>
        /// <param name="span">Source span containing the literal.</param>
        /// <param name="startIndex">Index of the opening <c>[</c>.</param>
        /// <param name="rangeEndIndex">Exclusive end of the domain range.</param>
        /// <param name="endIndex">Index after the closing <c>]</c> on success.</param>
        /// <returns><see langword="true"/> when the literal is well-formed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseDomainLiteral(ReadOnlySpan<char> span, int startIndex, int rangeEndIndex, out int endIndex)
        {
            endIndex = startIndex;
            if (startIndex >= rangeEndIndex || span[startIndex] != '[')
            {
                return false;
            }

            int index = startIndex + 1;
            while (index < rangeEndIndex)
            {
                char c = span[index];
                if (c == ']')
                {
                    endIndex = index + 1;
                    return index > startIndex + 1;
                }

                if (!MessageIdCharClasses.IsNorm(c))
                {
                    return false;
                }

                index++;
            }

            return false;
        }
    }
}
