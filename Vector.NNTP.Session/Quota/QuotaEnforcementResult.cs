// <copyright file="QuotaEnforcementResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Quota
{
    /// <summary>
    /// Outcome of post-command quota enforcement.
    /// </summary>
    /// <param name="ShouldDeauthorize">Whether the connection should clear authentication.</param>
    /// <param name="Reason">Stable reason code for logs (for example <c>block_quota</c>).</param>
    public readonly record struct QuotaEnforcementResult(bool ShouldDeauthorize, string Reason);
}
