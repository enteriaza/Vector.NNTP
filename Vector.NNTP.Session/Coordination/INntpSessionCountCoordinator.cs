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
        /// Gets the observed concurrent authenticated session count for an account.
        /// </summary>
        /// <param name="username">Authenticated username.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Session count (minimum 1 when called for shaping).</returns>
        public Task<long> GetSessionCountAsync(string username, CancellationToken cancellationToken);
    }
}
