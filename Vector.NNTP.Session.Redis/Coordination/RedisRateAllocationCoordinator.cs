// <copyright file="RedisRateAllocationCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Fair-share allocation using cluster-wide Redis session counts.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="RedisRateAllocationCoordinator"/> class.
    /// </remarks>
    /// <param name="sessionCountCoordinator">Cluster session count source.</param>
    /// <param name="rateOptions">Refresh cadence options.</param>
    /// <param name="logger">Logger.</param>
    public sealed partial class RedisRateAllocationCoordinator(
        INntpSessionCountCoordinator sessionCountCoordinator,
        IOptionsMonitor<NntpRateAllocationOptions> rateOptions,
        ILogger<RedisRateAllocationCoordinator> logger) : INntpRateAllocationCoordinator
    {
        private readonly INntpSessionCountCoordinator _sessionCountCoordinator = sessionCountCoordinator ?? throw new ArgumentNullException(nameof(sessionCountCoordinator));
        private readonly IOptionsMonitor<NntpRateAllocationOptions> _rateOptions = rateOptions ?? throw new ArgumentNullException(nameof(rateOptions));
        private readonly ILogger<RedisRateAllocationCoordinator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly Dictionary<string, (long Cap, DateTimeOffset NextRefresh)> _refreshState =
            new(StringComparer.Ordinal);

        private readonly object _refreshLock = new();

        /// <inheritdoc />
        public async Task<long> GetPerSessionSendRateBytesPerSecondAsync(
            NntpSessionPolicy policy,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (policy.AccountType != NntpAccountType.RateLimited || policy.RateBytesPerSecond <= 0)
            {
                return 0;
            }

            string accountKey = AccountKeyNormalizer.ComputeAccountKey(policy.Username);
            long sessions = await _sessionCountCoordinator.GetSessionCountAsync(policy.Username, cancellationToken).ConfigureAwait(false);
            sessions = Math.Max(1, sessions);
            long computed = RateAllocator.ComputePerSessionSendRateBytesPerSecond(policy.RateBytesPerSecond, sessions);
            NntpRateAllocationOptions opts = _rateOptions.CurrentValue;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (_refreshLock)
            {
                long previousCap = 0;
                if (_refreshState.TryGetValue(accountKey, out (long Cap, DateTimeOffset NextRefresh) entry))
                {
                    previousCap = entry.Cap;
                    if (now < entry.NextRefresh)
                    {
                        return entry.Cap;
                    }
                }

                long cap = computed;
                if (previousCap > 0)
                {
                    double delta = Math.Abs((double)(cap - previousCap) / previousCap);
                    if (delta < opts.MaterialRateChangeRatio)
                    {
                        DateTimeOffset next = now.Add(opts.RateAllocationRefreshInterval);
                        _refreshState[accountKey] = (previousCap, next);
                        return previousCap;
                    }
                }

                DateTimeOffset refreshAt = now.Add(opts.RateAllocationRefreshInterval);
                _refreshState[accountKey] = (cap, refreshAt);
                LogInformationAccountRateRebalanced(accountKey, sessions, cap, policy.RateBytesPerSecond);
                return cap;
            }
        }
    }
}
