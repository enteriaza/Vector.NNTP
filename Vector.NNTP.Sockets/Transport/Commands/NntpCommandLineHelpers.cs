// <copyright file="NntpCommandLineHelpers.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: shared NNTP command-line parsing helpers for per-verb handlers.

namespace Vector.NNTP.Sockets.Transport.Commands
{
    /// <summary>
    /// Parses NNTP command lines into verb and argument tokens for dispatch handlers.
    /// </summary>
    internal static class NntpCommandLineHelpers
    {
        /// <summary>
        /// Returns the argument portion of a command line after the first verb token.
        /// </summary>
        /// <param name="line">Full command line without CRLF.</param>
        /// <returns>Trimmed argument text, or <see langword="null"/> when no argument is present.</returns>
        internal static string? ExtractArgument(string line)
        {
            ArgumentNullException.ThrowIfNull(line);
            int index = 0;
            while (index < line.Length && !char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            return index < line.Length ? line[index..].Trim() : null;
        }

        /// <summary>
        /// Returns the first verb token from a command line.
        /// </summary>
        /// <param name="line">Full command line without CRLF.</param>
        /// <returns>The verb (first whitespace-delimited token).</returns>
        internal static string GetVerb(string line)
        {
            ArgumentNullException.ThrowIfNull(line);
            int end = 0;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
            {
                end++;
            }

            return line[..end];
        }

        /// <summary>
        /// Returns the argument portion of a command line after the first verb token.
        /// </summary>
        /// <param name="line">Full command line without CRLF.</param>
        /// <returns>Trimmed argument text, or an empty string when no argument is present.</returns>
        internal static string GetArgument(string line)
        {
            ArgumentNullException.ThrowIfNull(line);
            int index = 0;
            while (index < line.Length && !char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            return index < line.Length ? line[index..].Trim() : string.Empty;
        }
    }
}
