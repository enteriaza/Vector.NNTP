// <copyright file="NodeLocalRateAllocationCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Fair-share allocation using node-local <see cref="ISessionDatabase"/> authenticated counts.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NodeLocalRateAllocationCoordinator"/> class.
    /// </remarks>
    /// <param name="sessionDatabase">Node-local session rows.</param>
    /// <param name="options">Refresh cadence options.</param>
    /// <param name="logger">Logger.</param>
    public sealed partial class NodeLocalRateAllocationCoordinator(
        ISessionDatabase sessionDatabase,
        IOptionsMonitor<NntpRateAllocationOptions> options,
        ILogger<NodeLocalRateAllocationCoordinator> logger) : INntpRateAllocationCoordinator
    {
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));
        private readonly IOptionsMonitor<NntpRateAllocationOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
        private readonly ILogger<NodeLocalRateAllocationCoordinator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly Dictionary<string, (long Cap, DateTimeOffset NextRefresh)> _refreshState =
            new(StringComparer.Ordinal);

        private readonly object _refreshLock = new();

        /// <inheritdoc />
        public Task<long> GetPerSessionSendRateBytesPerSecondAsync(
            NntpSessionPolicy policy,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            cancellationToken.ThrowIfCancellationRequested();
            if (policy.AccountType != NntpAccountType.RateLimited || policy.RateBytesPerSecond <= 0)
            {
                return Task.FromResult(0L);
            }

            string accountKey = AccountKeyNormalizer.ComputeAccountKey(policy.Username);
            long sessions = Math.Max(1, _sessionDatabase.CountAuthenticatedByAccountKey(accountKey));
            long computed = RateAllocator.ComputePerSessionSendRateBytesPerSecond(policy.RateBytesPerSecond, sessions);
            NntpRateAllocationOptions opts = _options.CurrentValue;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (_refreshLock)
            {
                long previousCap = 0;
                if (_refreshState.TryGetValue(accountKey, out (long Cap, DateTimeOffset NextRefresh) entry))
                {
                    previousCap = entry.Cap;
                    if (now < entry.NextRefresh)
                    {
                        return Task.FromResult(entry.Cap);
                    }
                }

                long cap = computed;
                if (previousCap > 0)
                {
                    double delta = Math.Abs((double)(cap - previousCap) / previousCap);
                    if (delta < opts.MaterialRateChangeRatio)
                    {
                        DateTimeOffset next = now.Add(opts.RateAllocationRefreshInterval);
                        LogDebugAccountRateRebalanceSkipped(accountKey, (next - now).TotalMilliseconds);
                        _refreshState[accountKey] = (previousCap, next);
                        return Task.FromResult(previousCap);
                    }
                }

                DateTimeOffset refreshAt = now.Add(opts.RateAllocationRefreshInterval);
                _refreshState[accountKey] = (cap, refreshAt);
                LogInformationAccountRateRebalanced(
                    accountKey,
                    sessions,
                    cap,
                    policy.RateBytesPerSecond);
                return Task.FromResult(cap);
            }
        }
    }
}
