//-----------------------------------------------------------------------
// <copyright file="DnsAuthoritativeQuorum.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Computes how many authoritative name servers must return the expected TXT for quorum.
    /// </summary>
    internal static class DnsAuthoritativeQuorum
    {
        /// <summary>
        /// Minimum number of distinct NS that must answer with the expected TXT (ceiling of count × ratio, ratio clamped).
        /// </summary>
        /// <param name="nameServerCount">Authoritative server count (0 allowed).</param>
        /// <param name="quorumRatio">Ratio in [0.5, 1.0].</param>
        /// <returns>Required match count.</returns>
        public static int RequiredMatchCount(int nameServerCount, double quorumRatio)
        {
            if (nameServerCount <= 0)
            {
                return 0;
            }

            double q = Math.Clamp(quorumRatio, 0.5, 1.0);
            return (int)Math.Ceiling(nameServerCount * q);
        }
    }
}
