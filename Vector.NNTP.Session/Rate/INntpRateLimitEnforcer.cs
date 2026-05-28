// <copyright file="INntpRateLimitEnforcer.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Rate
{
    /// <summary>
    /// Applies outbound rate shaping to a session transport after authentication.
    /// </summary>
    public interface INntpRateLimitEnforcer
    {
        /// <summary>
        /// Wraps the session outbound stream with a dynamic send rate limiter when policy requires it.
        /// </summary>
        /// <param name="sessionId">Connection session identifier.</param>
        /// <param name="policy">Authenticated policy.</param>
        /// <param name="initialPerSessionBytesPerSecond">Initial fair-share cap.</param>
        /// <returns>Rate limiter handle for refresh, or <see langword="null"/> when disabled.</returns>
        public IDynamicSendRateLimiter? ApplyAfterAuthentication(
            string sessionId,
            NntpSessionPolicy policy,
            long initialPerSessionBytesPerSecond);
    }
}
