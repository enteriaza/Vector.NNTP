// <copyright file="NntpKnownVerb.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: known NNTP verbs for dispatch.

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Known NNTP command verbs handled by the dispatcher.
    /// </summary>
    internal enum NntpKnownVerb : byte
    {
        /// <summary>Unknown verb.</summary>
        Unknown = 0,

        /// <summary>QUIT.</summary>
        Quit,

        /// <summary>HELP.</summary>
        Help,

        /// <summary>DATE.</summary>
        Date,

        /// <summary>CAPABILITIES.</summary>
        Capabilities,

        /// <summary>MODE.</summary>
        Mode,

        /// <summary>STARTTLS.</summary>
        StartTls,

        /// <summary>COMPRESS.</summary>
        Compress,

        /// <summary>LIST.</summary>
        List,

        /// <summary>LISTGROUP.</summary>
        ListGroup,

        /// <summary>HDR / XHDR.</summary>
        Hdr,

        /// <summary>OVER / XOVER.</summary>
        Over,

        /// <summary>GROUP.</summary>
        Group,

        /// <summary>ARTICLE / HEAD / BODY / STAT.</summary>
        Article,

        /// <summary>NEXT.</summary>
        Next,

        /// <summary>LAST.</summary>
        Last,

        /// <summary>POST.</summary>
        Post,

        /// <summary>CHECK (streaming filter).</summary>
        Check,

        /// <summary>IHAVE (offer and body transfer).</summary>
        Ihave,

        /// <summary>TAKETHIS.</summary>
        Takethis,

        /// <summary>AUTHINFO.</summary>
        Authinfo,

        /// <summary>NEWGROUPS (not implemented; returns 503).</summary>
        Newgroups,

        /// <summary>NEWNEWS (not implemented; returns 503).</summary>
        Newnews,

        /// <summary>SLAVE (not implemented; returns 503).</summary>
        Slave,
    }
}
