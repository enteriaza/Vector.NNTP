// <copyright file="RateAllocator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Rate
{
    /// <summary>
    /// Computes per-session fair-share send rates from aggregate account limits.
    /// </summary>
    /// <remarks>
    /// <para><b>Fair-share:</b> The observed session count includes every live authenticated TCP on the node or cluster
    /// (idle included). The account ceiling is divided by session count, never multiplied.</para>
    /// </remarks>
    public static class RateAllocator
    {
        /// <summary>
        /// Divides the account send rate by the observed authenticated session count (minimum divisor 1).
        /// </summary>
        /// <param name="accountRateBytesPerSecond">Aggregate account rate; 0 means unlimited.</param>
        /// <param name="observedSessions">Active authenticated session count.</param>
        /// <returns>Per-session send rate in bytes per second; 0 when account rate is unlimited/disabled.</returns>
        public static long ComputePerSessionSendRateBytesPerSecond(long accountRateBytesPerSecond, long observedSessions)
        {
            if (accountRateBytesPerSecond <= 0)
            {
                return 0;
            }

            long effectiveSessions = Math.Max(1, observedSessions);
            return accountRateBytesPerSecond / effectiveSessions;
        }
    }
}
