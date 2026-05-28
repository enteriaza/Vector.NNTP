// <copyright file="InMemorySessionCountCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Node-local session count for tests when Redis is not registered.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="InMemorySessionCountCoordinator"/> class.
    /// </remarks>
    /// <param name="sessionDatabase">Session store.</param>
    public sealed class InMemorySessionCountCoordinator(ISessionDatabase sessionDatabase) : INntpSessionCountCoordinator
    {
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));

        /// <inheritdoc />
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
