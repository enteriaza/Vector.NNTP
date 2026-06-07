// <copyright file="HeaderFieldValidation.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: INN headers.c-style header field validation for article preprocessing.

namespace Vector.NNTP.Utilities.Validation
{
    /// <summary>
    /// Validates NNTP/USENET header field names, bodies, and full header lines per RFC 3977.
    /// </summary>
    /// <remarks>
    /// <para>Used by article spool preprocessing on the writer path — not on socket command threads.</para>
    /// </remarks>
    public static class HeaderFieldValidation
    {
        /// <summary>
        /// Determines whether <paramref name="name"/> is a valid header field name.
        /// </summary>
        /// <param name="name">Header name without trailing colon.</param>
        /// <returns>
        /// <see langword="true"/> when the name is non-empty printable US-ASCII without colon characters.
        /// </returns>
        public static bool IsValidHeaderName(ReadOnlySpan<char> name)
        {
            if (name.IsEmpty)
            {
                return false;
            }

            foreach (char c in name)
            {
                if (!IsGraph(c) || c == ':')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether <paramref name="name"/> is a valid header field name.
        /// </summary>
        /// <param name="name">Header name without trailing colon.</param>
        /// <returns>
        /// <see langword="true"/> when the name is non-empty printable US-ASCII without colon characters.
        /// </returns>
        public static bool IsValidHeaderName(string? name)
        {
            return !string.IsNullOrEmpty(name) && IsValidHeaderName(name.AsSpan());
        }

        /// <summary>
        /// Determines whether <paramref name="body"/> is a valid header field body (value after <c>name: </c>).
        /// </summary>
        /// <param name="body">Header body including folded continuation lines, as raw UTF-8 bytes.</param>
        /// <returns>
        /// <see langword="true"/> when the body is valid UTF-8 with correct folding rules and non-empty content.
        /// </returns>
        private static bool IsValidHeaderBody(ReadOnlySpan<byte> body)
        {
            if (body.IsEmpty)
            {
                return false;
            }

            if (!System.Text.Unicode.Utf8.IsValid(body))
            {
                return false;
            }

            bool emptyContentLine = true;
            int index = 0;
            while (index < body.Length)
            {
                byte b = body[index];
                if (b is (byte)' ' or (byte)'\t')
                {
                    index++;
                    continue;
                }

                if (b == (byte)'\n' || (b == (byte)'\r' && index + 1 < body.Length && body[index + 1] == (byte)'\n'))
                {
                    if (emptyContentLine)
                    {
                        return false;
                    }

                    if (b == (byte)'\r')
                    {
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }

                    if (index >= body.Length || body[index] is not ((byte)' ' or (byte)'\t'))
                    {
                        return false;
                    }

                    emptyContentLine = true;
                    continue;
                }

                if (index > 0 && body[index - 1] == (byte)'\r')
                {
                    return false;
                }

                emptyContentLine = false;
                index++;
            }

            return !emptyContentLine;
        }

        /// <summary>
        /// Determines whether <paramref name="body"/> is a valid header field body (value after <c>name: </c>).
        /// </summary>
        /// <param name="body">Header body including folded continuation lines.</param>
        /// <returns>
        /// <see langword="true"/> when the body is valid UTF-8 with correct folding rules and non-empty content.
        /// </returns>
        public static bool IsValidHeaderBody(ReadOnlySpan<char> body)
        {
            if (body.IsEmpty)
            {
                return false;
            }

            if (!IsValidUtf8(body))
            {
                return false;
            }

            bool emptyContentLine = true;
            int index = 0;
            while (index < body.Length)
            {
                char c = body[index];
                if (c is ' ' or '\t')
                {
                    index++;
                    continue;
                }

                if (c == '\n' || (c == '\r' && index + 1 < body.Length && body[index + 1] == '\n'))
                {
                    if (emptyContentLine)
                    {
                        return false;
                    }

                    if (c == '\r')
                    {
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }

                    if (index >= body.Length || body[index] is not (' ' or '\t'))
                    {
                        return false;
                    }

                    emptyContentLine = true;
                    continue;
                }

                if (index > 0 && body[index - 1] == '\r')
                {
                    return false;
                }

                emptyContentLine = false;
                index++;
            }

            return !emptyContentLine;
        }

        /// <summary>
        /// Determines whether <paramref name="body"/> is a valid header field body.
        /// </summary>
        /// <param name="body">Header body including folded continuation lines.</param>
        /// <returns>
        /// <see langword="true"/> when the body is valid UTF-8 with correct folding rules and non-empty content.
        /// </returns>
        public static bool IsValidHeaderBody(string? body)
        {
            return !string.IsNullOrEmpty(body) && IsValidHeaderBody(body.AsSpan());
        }

        /// <summary>
        /// Determines whether <paramref name="field"/> is a valid header field line (<c>name: value</c>).
        /// </summary>
        /// <param name="field">Full header field line without trailing CRLF, as raw UTF-8 bytes.</param>
        /// <returns><see langword="true"/> when name, colon, required space, and body are valid.</returns>
        /// <remarks>
        /// Allocation-free path for spool preprocessing. Header names must be printable US-ASCII; bodies must be
        /// well-formed UTF-8 with correct folding rules.
        /// </remarks>
        public static bool IsValidHeaderField(ReadOnlySpan<byte> field)
        {
            if (field.IsEmpty || field[0] == (byte)':')
            {
                return false;
            }

            int index = 0;
            while (index < field.Length)
            {
                byte b = field[index];
                if (!IsGraph(b))
                {
                    return false;
                }

                if (b == (byte)':')
                {
                    index++;
                    break;
                }

                index++;
            }

            if (index >= field.Length || field[index] != (byte)' ')
            {
                return false;
            }

            index++;
            return IsValidHeaderBody(field[index..]);
        }

        /// <summary>
        /// Determines whether <paramref name="field"/> is a valid header field line (<c>name: value</c>).
        /// </summary>
        /// <param name="field">Full header field line without trailing CRLF.</param>
        /// <returns><see langword="true"/> when name, colon, required space, and body are valid.</returns>
        public static bool IsValidHeaderField(ReadOnlySpan<char> field)
        {
            if (field.IsEmpty || field[0] == ':')
            {
                return false;
            }

            int index = 0;
            while (index < field.Length)
            {
                char c = field[index];
                if (!IsGraph(c))
                {
                    return false;
                }

                if (c == ':')
                {
                    index++;
                    break;
                }

                index++;
            }

            if (index >= field.Length || field[index] != ' ')
            {
                return false;
            }

            index++;
            return IsValidHeaderBody(field[index..]);
        }

        /// <summary>
        /// Determines whether <paramref name="field"/> is a valid header field line.
        /// </summary>
        /// <param name="field">Full header field line without trailing CRLF.</param>
        /// <returns><see langword="true"/> when name, colon, required space, and body are valid.</returns>
        public static bool IsValidHeaderField(string? field)
        {
            return !string.IsNullOrEmpty(field) && IsValidHeaderField(field.AsSpan());
        }

        /// <summary>
        /// Returns whether <paramref name="c"/> is a printable US-ASCII character (RFC 3977 graph).
        /// </summary>
        /// <param name="c">Character to test.</param>
        /// <returns><see langword="true"/> for printable US-ASCII.</returns>
        private static bool IsGraph(char c)
        {
            return c is >= (char)33 and <= (char)126;
        }

        /// <summary>
        /// Returns whether <paramref name="b"/> is a printable US-ASCII byte (RFC 3977 graph).
        /// </summary>
        /// <param name="b">Byte to test.</param>
        /// <returns><see langword="true"/> for printable US-ASCII.</returns>
        private static bool IsGraph(byte b)
        {
            return b is >= (byte)33 and <= (byte)126;
        }

        /// <summary>
        /// Validates that <paramref name="text"/> is well-formed UTF-8 (INN <c>is_valid_utf8</c> equivalent).
        /// </summary>
        /// <param name="text">Text span to validate.</param>
        /// <returns><see langword="true"/> when the span encodes valid UTF-8.</returns>
        private static bool IsValidUtf8(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty)
            {
                return true;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsSurrogate(c))
                {
                    continue;
                }

                if (i + 1 >= text.Length || !char.IsSurrogatePair(c, text[i + 1]))
                {
                    return false;
                }

                i++;
            }

            return true;
        }
    }
}
