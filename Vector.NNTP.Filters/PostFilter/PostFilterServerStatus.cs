// <copyright file="PostFilterServerStatus.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterServerStatus.cs -- Global posting gate for the PostFilter pipeline.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Global posting gate (maps to Perl <c>$config{'server_status'}</c>).
    /// </summary>
    public enum PostFilterServerStatus
    {
        /// <summary>Normal filtering pipeline runs.</summary>
        Active = 0,

        /// <summary>All posts rejected (Perl code 48).</summary>
        Closed = 1,

        /// <summary>All posts accepted without checks (dangerous; matches Perl <c>disabled</c>).</summary>
        Disabled = 2,
    }
}

