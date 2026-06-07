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
        /// Remaining byte quotas keyed by normalized account key.
        /// </summary>
        private readonly ConcurrentDictionary<string, long> _quotas = new(StringComparer.Ordinal);

        /// <summary>
        /// Attempts to seed the initial quota for an account key.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="initialBytes">Initial remaining bytes.</param>
        /// <param name="cancellationToken">Cancellation token (unused in-memory).</param>
        /// <returns><see langword="true"/> when the key was newly inserted.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountKey"/> is null or empty.</exception>
        public ValueTask<bool> TryInitializeQuotaAsync(string accountKey, long initialBytes, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            return ValueTask.FromResult(_quotas.TryAdd(accountKey, initialBytes));
        }

        /// <summary>
        /// Decrements the remaining quota and returns the new balance.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="bytes">Bytes to subtract from the quota.</param>
        /// <param name="cancellationToken">Cancellation token (unused in-memory).</param>
        /// <returns>Remaining bytes after decrement (may be negative).</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountKey"/> is null or empty.</exception>
        public ValueTask<long> DecrementAsync(string accountKey, long bytes, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            long remaining = _quotas.AddOrUpdate(accountKey, 0, (_, current) => current - bytes);
            return ValueTask.FromResult(remaining);
        }
    }
}
