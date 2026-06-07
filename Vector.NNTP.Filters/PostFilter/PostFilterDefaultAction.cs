// <copyright file="PostFilterDefaultAction.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// PostFilterDefaultAction.cs -- Perl default_action_on_accept / default_action_on_reject behavior flags.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Perl <c>default_action_on_accept</c> / <c>default_action_on_reject</c> behavior (NNTP client-visible outcome may differ).
    /// </summary>
    /// <remarks>
    /// <para>Applied by the postfilter engine after all checks complete; maps to <see cref="PostFilterResult"/> success/drop flags.</para>
    /// </remarks>
    public enum PostFilterDefaultAction
    {
        /// <summary>
        /// Default non-inverting accept/reject semantics for the configured postfilter profile.
        /// </summary>
        /// <remarks>
        /// Filter outcomes map directly to <see cref="PostFilterResult.ClientShouldSeeSuccess"/> without inversion.
        /// </remarks>
        Accept = 0,

        /// <summary>Silently drop after reporting success to the client (Perl <c>DROP</c>).</summary>
        Discard = 1,

        /// <summary>Reserved for future spool save; currently treated like discard for storage (not implemented).</summary>
        Save = 2,

        /// <summary>Invert outcome (accept path rejects, reject path accepts).</summary>
        Reject = 3,
    }
}

