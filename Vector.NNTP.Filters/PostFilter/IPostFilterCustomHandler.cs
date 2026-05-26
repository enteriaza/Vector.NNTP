// <copyright file="IPostFilterCustomHandler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// IPostFilterCustomHandler.cs -- Optional extension point for custom postfilter rules.

namespace Vector.NNTP.Filters.PostFilter
{
    /// <summary>
    /// Optional extension point (Perl <c>custom.pm</c>). Register one or more implementations in DI when
    /// <see cref="PostFilterOptions.CheckCustom"/> is true.
    /// </summary>
    /// <remarks>
    /// <para>Handlers run in registration order; the first non-<see langword="null"/> rejection code stops the pipeline.
    /// Use <see cref="PostFilterRejectionMessages"/> for consistent client text unless a custom message is required.</para>
    /// </remarks>
    public interface IPostFilterCustomHandler
    {
        /// <summary>
        /// Runs custom logic after built-in checks in the main pipeline order.
        /// </summary>
        /// <param name="context">Client identity and clock supplied by the NNTP host.</param>
        /// <param name="article">Parsed headers and body for inspection or rewrite.</param>
        /// <param name="cancellationToken">Cancellation for long-running custom checks (DNS, external APIs).</param>
        /// <returns><see langword="null"/> to continue; otherwise a numeric rejection code (for example 11 for custom reject).</returns>
        public ValueTask<int?> EvaluateAsync(PostFilterContext context, PostFilterParsedArticle article, CancellationToken cancellationToken);
    }
}

