// <copyright file="NntpCommandVerb.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: span-based verb classification without allocating uppercase strings.

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Parses NNTP command verbs from a command line without allocating uppercase strings.
    /// </summary>
    internal static class NntpCommandVerb
    {
        /// <summary>
        /// Classifies the verb on a command line (leading whitespace trimmed).
        /// </summary>
        /// <param name="line">Full command line without CRLF.</param>
        /// <returns>Known verb classification.</returns>
        internal static NntpKnownVerb Classify(ReadOnlySpan<char> line)
        {
            int start = 0;
            while (start < line.Length && char.IsWhiteSpace(line[start]))
            {
                start++;
            }

            if (start >= line.Length)
            {
                return NntpKnownVerb.Unknown;
            }

            int end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
            {
                end++;
            }

            return ClassifyVerb(line[start..end]);
        }

        private static NntpKnownVerb ClassifyVerb(ReadOnlySpan<char> verb)
        {
            switch (verb.Length)
            {
                case 3:
                    if (IsVerb(verb, "HDR")) return NntpKnownVerb.Hdr;
                    break;
                case 4:
                    if (IsVerb(verb, "QUIT")) return NntpKnownVerb.Quit;
                    if (IsVerb(verb, "HELP")) return NntpKnownVerb.Help;
                    if (IsVerb(verb, "DATE")) return NntpKnownVerb.Date;
                    if (IsVerb(verb, "MODE")) return NntpKnownVerb.Mode;
                    if (IsVerb(verb, "LIST")) return NntpKnownVerb.List;
                    if (IsVerb(verb, "OVER")) return NntpKnownVerb.Over;
                    if (IsVerb(verb, "POST")) return NntpKnownVerb.Post;
                    if (IsVerb(verb, "NEXT")) return NntpKnownVerb.Next;
                    if (IsVerb(verb, "LAST")) return NntpKnownVerb.Last;
                    if (IsVerb(verb, "HEAD")) return NntpKnownVerb.Article;
                    if (IsVerb(verb, "BODY")) return NntpKnownVerb.Article;
                    if (IsVerb(verb, "STAT")) return NntpKnownVerb.Article;
                    break;
                case 5:
                    if (IsVerb(verb, "GROUP")) return NntpKnownVerb.Group;
                    if (IsVerb(verb, "CHECK")) return NntpKnownVerb.Check;
                    if (IsVerb(verb, "IHAVE")) return NntpKnownVerb.Check;
                    if (IsVerb(verb, "XOVER")) return NntpKnownVerb.Over;
                    if (IsVerb(verb, "SLAVE")) return NntpKnownVerb.Slave;
                    break;
                case 6:
                    if (IsVerb(verb, "XHDR")) return NntpKnownVerb.Hdr;
                    break;
                case 7:
                    if (IsVerb(verb, "ARTICLE")) return NntpKnownVerb.Article;
                    if (IsVerb(verb, "NEWNEWS")) return NntpKnownVerb.Newnews;
                    break;
                case 8:
                    if (IsVerb(verb, "STARTTLS")) return NntpKnownVerb.StartTls;
                    if (IsVerb(verb, "AUTHINFO")) return NntpKnownVerb.Authinfo;
                    if (IsVerb(verb, "COMPRESS")) return NntpKnownVerb.Compress;
                    if (IsVerb(verb, "TAKETHIS")) return NntpKnownVerb.Takethis;
                    break;
                case 9:
                    if (IsVerb(verb, "LISTGROUP")) return NntpKnownVerb.ListGroup;
                    if (IsVerb(verb, "NEWGROUPS")) return NntpKnownVerb.Newgroups;
                    break;
                case 12:
                    if (IsVerb(verb, "CAPABILITIES")) return NntpKnownVerb.Capabilities;
                    break;
            }

            return NntpKnownVerb.Unknown;
        }

        private static bool IsVerb(ReadOnlySpan<char> verb, string expected)
        {
            if (verb.Length != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < verb.Length; i++)
            {
                if (char.ToUpperInvariant(verb[i]) != expected[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
