// <copyright file="IDynamicSendRateLimiter.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Rate
{
    /// <summary>
    /// Handle to update per-session send caps without re-wrapping the transport.
    /// </summary>
    public interface IDynamicSendRateLimiter
    {
        /// <summary>
        /// Gets the current cap.
        /// </summary>
        public long MaxSendBytesPerSecond { get; }

        /// <summary>
        /// Updates the maximum send bytes per second when materially changed.
        /// </summary>
        /// <param name="newMaxSendBytesPerSecond">New cap.</param>
        public void UpdateMaxSendBytesPerSecond(long newMaxSendBytesPerSecond);
    }
}
