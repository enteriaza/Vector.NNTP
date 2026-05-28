// <copyright file="NntpCommandVerbBytes.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: ASCII byte verb classification without string allocation.

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Classifies NNTP command verbs from an ASCII byte line without allocating.
    /// </summary>
    /// <remarks>
    /// This implementation intentionally relies on predictable branching and simple ASCII folding.
    /// The .NET runtime already vectorizes common span operations; introduce intrinsics only when profiling proves a material win.
    /// </remarks>
    internal static class NntpCommandVerbBytes
    {
        /// <summary>
        /// Classifies the verb on a command line (leading ASCII whitespace trimmed).
        /// </summary>
        /// <param name="line">Full command line bytes without CRLF.</param>
        /// <returns>Known verb classification.</returns>
        internal static NntpKnownVerb Classify(ReadOnlySpan<byte> line)
        {
            int start = 0;
            while (start < line.Length && IsAsciiWhitespace(line[start]))
            {
                start++;
            }

            if (start >= line.Length)
            {
                return NntpKnownVerb.Unknown;
            }

            int end = start;
            while (end < line.Length && !IsAsciiWhitespace(line[end]))
            {
                end++;
            }

            return ClassifyVerb(line.Slice(start, end - start));
        }

        private static NntpKnownVerb ClassifyVerb(ReadOnlySpan<byte> verb)
        {
            return verb.Length switch
            {
                3 => IsVerb(verb, "HDR") ? NntpKnownVerb.Hdr : NntpKnownVerb.Unknown,
                4 => IsVerb(verb, "QUIT") ? NntpKnownVerb.Quit
                    : IsVerb(verb, "HELP") ? NntpKnownVerb.Help
                    : IsVerb(verb, "DATE") ? NntpKnownVerb.Date
                    : IsVerb(verb, "MODE") ? NntpKnownVerb.Mode
                    : IsVerb(verb, "LIST") ? NntpKnownVerb.List
                    : IsVerb(verb, "OVER") ? NntpKnownVerb.Over
                    : IsVerb(verb, "POST") ? NntpKnownVerb.Post
                    : IsVerb(verb, "NEXT") ? NntpKnownVerb.Next
                    : IsVerb(verb, "LAST") ? NntpKnownVerb.Last
                    : IsVerb(verb, "HEAD") ? NntpKnownVerb.Article
                    : IsVerb(verb, "BODY") ? NntpKnownVerb.Article
                    : IsVerb(verb, "STAT") ? NntpKnownVerb.Article
                    : NntpKnownVerb.Unknown,
                5 => IsVerb(verb, "GROUP") ? NntpKnownVerb.Group
                    : IsVerb(verb, "CHECK") ? NntpKnownVerb.Check
                    : IsVerb(verb, "IHAVE") ? NntpKnownVerb.Check
                    : IsVerb(verb, "XOVER") ? NntpKnownVerb.Over
                    : IsVerb(verb, "SLAVE") ? NntpKnownVerb.Slave
                    : NntpKnownVerb.Unknown,
                6 => IsVerb(verb, "XHDR") ? NntpKnownVerb.Hdr : NntpKnownVerb.Unknown,
                7 => IsVerb(verb, "ARTICLE") ? NntpKnownVerb.Article
                    : IsVerb(verb, "NEWNEWS") ? NntpKnownVerb.Newnews
                    : NntpKnownVerb.Unknown,
                8 => IsVerb(verb, "STARTTLS") ? NntpKnownVerb.StartTls
                    : IsVerb(verb, "AUTHINFO") ? NntpKnownVerb.Authinfo
                    : IsVerb(verb, "COMPRESS") ? NntpKnownVerb.Compress
                    : IsVerb(verb, "TAKETHIS") ? NntpKnownVerb.Takethis
                    : NntpKnownVerb.Unknown,
                9 => IsVerb(verb, "LISTGROUP") ? NntpKnownVerb.ListGroup
                    : IsVerb(verb, "NEWGROUPS") ? NntpKnownVerb.Newgroups
                    : NntpKnownVerb.Unknown,
                12 => IsVerb(verb, "CAPABILITIES") ? NntpKnownVerb.Capabilities : NntpKnownVerb.Unknown,
                _ => NntpKnownVerb.Unknown,
            };
        }

        private static bool IsVerb(ReadOnlySpan<byte> verb, string expected)
        {
            if (verb.Length != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < verb.Length; i++)
            {
                byte b = verb[i];
                if (b >= (byte)'a' && b <= (byte)'z')
                {
                    b = (byte)(b - 32);
                }

                if (b != (byte)expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiWhitespace(byte b) => b == (byte)' ' || b == (byte)'\t';
    }
}

