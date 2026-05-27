// <copyright file="NntpSessionMode.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: session mode enumeration per RFC 3977 / 4644.

namespace Vector.NNTP.Sockets.Session
{
    /// <summary>
    /// Active NNTP session mode after successful MODE negotiation.
    /// </summary>
    public enum NntpSessionMode
    {
        /// <summary>
        /// No MODE READER or MODE STREAM has been selected yet.
        /// </summary>
        None = 0,

        /// <summary>
        /// Reader mode (RFC 3977) after <c>MODE READER</c>.
        /// </summary>
        Reader = 1,

        /// <summary>
        /// Streaming transit mode (RFC 4644) after <c>MODE STREAM</c>.
        /// </summary>
        Stream = 2,
    }
}
