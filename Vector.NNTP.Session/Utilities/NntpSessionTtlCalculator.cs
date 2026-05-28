// <copyright file="NntpSessionTtlCalculator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Utilities
{
    /// <summary>
    /// Computes Redis lease TTL from resolved socket idle timeout.
    /// </summary>
    public static class NntpSessionTtlCalculator
    {
        /// <summary>
        /// Minimum lease TTL in seconds.
        /// </summary>
        public const int MinimumTtlSeconds = 300;

        /// <summary>
        /// Computes <c>max(300, ceil(idleTimeoutSeconds * 2))</c>.
        /// </summary>
        /// <param name="idleTimeout">Resolved NNTP idle timeout (same as socket enforcement).</param>
        /// <returns>TTL seconds for acquire and heartbeat.</returns>
        public static int ComputeTtlSeconds(TimeSpan idleTimeout)
        {
            double seconds = idleTimeout.TotalSeconds;
            int scaled = (int)Math.Ceiling(seconds * 2.0);
            return Math.Max(MinimumTtlSeconds, scaled);
        }
    }
}
