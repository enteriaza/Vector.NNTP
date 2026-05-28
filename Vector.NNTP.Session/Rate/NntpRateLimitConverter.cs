// <copyright file="NntpRateLimitConverter.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Rate
{
    /// <summary>
    /// Converts operator-facing rate values to wire enforcement units.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mbps unit:</b> MySQL <c>account_rate_limit</c> is interpreted as decimal SI megabits per second (Mbps),
    /// not binary mebibits per second (Mibps). One Mbps = 1,000,000 bits/s = 125,000 bytes/s.
    /// </para>
    /// </remarks>
    public static class NntpRateLimitConverter
    {
        /// <summary>
        /// Bits per decimal SI megabit (1 Mbps = 10⁶ bit/s).
        /// </summary>
        private const long BitsPerMegabit = 1_000_000L;

        /// <summary>
        /// Bits per byte.
        /// </summary>
        private const long BitsPerByte = 8L;

        /// <summary>
        /// Converts decimal SI megabits per second to bytes per second.
        /// </summary>
        /// <param name="megabitsPerSecond">Rate in decimal SI Mbps from the database.</param>
        /// <returns>Bytes per second for enforcement; <c>0</c> when input is non-positive.</returns>
        public static long MegabitsPerSecondToBytesPerSecond(int megabitsPerSecond)
        {
            return megabitsPerSecond <= 0 ? 0 : megabitsPerSecond * BitsPerMegabit / BitsPerByte;
        }
    }
}
