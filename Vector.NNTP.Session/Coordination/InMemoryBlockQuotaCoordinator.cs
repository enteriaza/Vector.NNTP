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
        private readonly ConcurrentDictionary<string, long> _quotas = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public ValueTask<bool> TryInitializeQuotaAsync(string accountKey, long initialBytes, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            return ValueTask.FromResult(_quotas.TryAdd(accountKey, initialBytes));
        }

        /// <inheritdoc />
        public ValueTask<long> DecrementAsync(string accountKey, long bytes, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            long remaining = _quotas.AddOrUpdate(accountKey, 0, (_, current) => current - bytes);
            return ValueTask.FromResult(remaining);
        }
    }
}
