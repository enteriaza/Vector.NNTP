// <copyright file="INntpSessionCountCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Observes cluster-wide active authenticated session counts for fair-share rate division.
    /// </summary>
    /// <remarks>
    /// An <b>active authenticated session</b> is every live authed TCP (idle counts; not transfer-active only).
    /// Counts are eventually consistent across nodes.
    /// </remarks>
    public interface INntpSessionCountCoordinator
    {
        /// <summary>
        /// Observes the concurrent authenticated session count for an account across the cluster.
        /// </summary>
        /// <param name="username">Authenticated username.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Session count used as fair-share divisor (implementations clamp to at least 1).</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public Task<long> GetSessionCountAsync(string username, CancellationToken cancellationToken);
    }
}
