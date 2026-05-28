// <copyright file="InMemoryBlockQuotaCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Collections.Concurrent;

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// In-memory block quota for tests.
    /// </summary>
    public sealed class InMemoryBlockQuotaCoordinator : INntpBlockQuotaCoordinator
    {
        /// <summary>
        /// Quotas dictionary.
        /// </summary>
        private readonly ConcurrentDictionary<string, long> _quotas = new(StringComparer.Ordinal);

        /// <summary>
        /// Tries to initialize a quota.
        /// </summary>
        /// <param name="accountKey">The account key.</param>
        /// <param name="initialBytes">The initial bytes.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the quota is initialized.</returns>
        public ValueTask<bool> TryInitializeQuotaAsync(string accountKey, long initialBytes, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            return ValueTask.FromResult(_quotas.TryAdd(accountKey, initialBytes));
        }

        /// <summary>
        /// Decrements a quota.
        /// </summary>
        /// <param name="accountKey">The account key.</param>
        /// <param name="bytes">The bytes to decrement.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the quota is decremented.</returns>
        public ValueTask<long> DecrementAsync(string accountKey, long bytes, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            long remaining = _quotas.AddOrUpdate(accountKey, 0, (_, current) => current - bytes);
            return ValueTask.FromResult(remaining);
        }
    }
}
