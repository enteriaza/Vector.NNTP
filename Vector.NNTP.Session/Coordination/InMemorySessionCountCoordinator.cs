// <copyright file="InMemorySessionCountCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Node-local session count for tests when Redis is not registered.
    /// </summary>
    /// <remarks>
    /// <para>Counts authenticated sessions on the injected <see cref="ISessionDatabase"/> only; does not observe other
    /// cluster nodes.</para>
    /// </remarks>
    /// <param name="sessionDatabase">Session store supplying per-node authenticated counts.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sessionDatabase"/> is null.</exception>
    public sealed class InMemorySessionCountCoordinator(ISessionDatabase sessionDatabase) : INntpSessionCountCoordinator
    {
        /// <summary>
        /// Node-local session database used for authenticated session counting.
        /// </summary>
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));

        /// <summary>
        /// Returns the node-local authenticated session count for the account, clamped to at least 1.
        /// </summary>
        /// <param name="username">Authenticated username.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Authenticated session count on this node (minimum 1 for fair-share division).</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public Task<long> GetSessionCountAsync(string username, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);
            cancellationToken.ThrowIfCancellationRequested();
            string accountKey = AccountKeyNormalizer.ComputeAccountKey(username);
            long count = _sessionDatabase.CountAuthenticatedByAccountKey(accountKey);
            return Task.FromResult(Math.Max(1, count));
        }
    }
}
