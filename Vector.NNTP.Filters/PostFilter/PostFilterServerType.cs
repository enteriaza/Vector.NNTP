// <copyright file="PostFilterServerType.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterServerType.cs -- Whether access limits key on IP, authenticated identity, or both.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Whether access limits key on IP, authenticated identity, or both (maps to Perl <c>server_type</c>).
    /// </summary>
    public enum PostFilterServerType
    {
        /// <summary>Unauthenticated readers; limits use client IP.</summary>
        Public = 0,

        /// <summary>All posters authenticated; limits use username.</summary>
        Auth = 1,

        /// <summary>Mixed; public users identified by <see cref="PostFilterOptions.PublicUserIdPattern"/>.</summary>
        Both = 2,
    }
}

