// <copyright file="NntpAccountLimits.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Policy
{
    /// <summary>
    /// Raw account limit columns mapped from persistence before policy construction.
    /// </summary>
    /// <param name="Username">Authenticated account name.</param>
    /// <param name="AccountTypeChar">MySQL <c>account_type</c> (<c>'R'</c> or <c>'B'</c>).</param>
    /// <param name="RateLimitMbps">
    /// MySQL <c>account_rate_limit</c> in decimal SI Mbps (not Mibps); active when <paramref name="AccountTypeChar"/> is <c>'R'</c>.
    /// </param>
    /// <param name="ByteLimit">MySQL <c>account_byte_limit</c>; active when <paramref name="AccountTypeChar"/> is <c>'B'</c>.</param>
    /// <param name="SessionLimit">Maximum concurrent authenticated sessions cluster-wide; <c>0</c> disables.</param>
    /// <param name="SrcIpLimit">Maximum distinct client IPs with concurrent sessions; <c>0</c> disables.</param>
    /// <param name="CustomerId">Customer identifier (UUID string).</param>
    public sealed record NntpAccountLimits(
        string Username,
        char AccountTypeChar,
        int RateLimitMbps,
        long ByteLimit,
        int SessionLimit,
        int SrcIpLimit,
        string CustomerId);
}
