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

        /// <summary>
        /// Computes the stale-member cutoff for transit peer Redis ZSET admission (RFC 4644 peering caps).
        /// </summary>
        /// <remarks>
        /// Uses roughly three heartbeat intervals (not socket idle timeout) so dead peers release slots within minutes,
        /// not after multi-hour idle TTLs.
        /// </remarks>
        /// <param name="heartbeatIntervalSeconds">Configured Redis heartbeat interval.</param>
        /// <param name="ttlMinimumSeconds">Configured minimum lease floor.</param>
        /// <returns>Seconds of score age after which a ZSET member is purged before <c>ZCARD</c>.</returns>
        public static int ComputeTransitPeerLeaseSeconds(
            int heartbeatIntervalSeconds = 60,
            int ttlMinimumSeconds = MinimumTtlSeconds)
        {
            int interval = Math.Max(1, heartbeatIntervalSeconds);
            int minimum = Math.Max(60, ttlMinimumSeconds);
            int scaled = interval * 3;
            return Math.Max(minimum, scaled);
        }

        /// <summary>
        /// Computes Redis EXPIRE seconds for <c>session:{id}</c> and <c>node:{node}:sessions</c> metadata keys.
        /// </summary>
        /// <param name="leaseSeconds">Coordination lease seconds from <see cref="ComputeTtlSeconds"/>.</param>
        /// <returns>Metadata TTL seconds (twice the coordination lease).</returns>
        public static int ComputeMetadataTtlSeconds(int leaseSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseSeconds);
            long doubled = (long)leaseSeconds * 2L;
            return doubled > int.MaxValue ? int.MaxValue : (int)doubled;
        }
    }
}
