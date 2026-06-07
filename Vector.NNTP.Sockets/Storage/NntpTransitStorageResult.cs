// <copyright file="NntpTransitStorageResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Sockets.Storage
{
    /// <summary>
    /// Outcome of enqueueing an article into transit spool storage.
    /// </summary>
    public enum NntpTransitStorageResult
    {
        /// <summary>
        /// Article was accepted into the bounded spool queue.
        /// </summary>
        Success,

        /// <summary>
        /// Spool queue rejected the article because item-count or byte-budget limits were exceeded.
        /// </summary>
        QueueFull,

        /// <summary>
        /// Storage rejected the article without enqueueing (for example decoded size exceeds
        /// <see cref="Configuration.NntpServerOptions.MaxArtSize"/>).
        /// </summary>
        ArticleRejected,
    }
}
