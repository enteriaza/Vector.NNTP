// <copyright file="NntpAccountType.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Policy
{
    /// <summary>
    /// Primary billing and enforcement model for an NNTP account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Maps from the MySQL <c>account_type</c> column: <c>'R'</c> is <see cref="RateLimited"/>;
    /// <c>'B'</c> is <see cref="ByteLimited"/>. These are not reader/transit role flags.
    /// </para>
    /// </remarks>
    public enum NntpAccountType
    {
        /// <summary>
        /// Account quota is enforced as an aggregate outbound rate (decimal SI Mbps → bytes/sec).
        /// </summary>
        RateLimited,

        /// <summary>
        /// Account quota is enforced as a cluster-wide byte budget decremented per command.
        /// </summary>
        ByteLimited,
    }
}
