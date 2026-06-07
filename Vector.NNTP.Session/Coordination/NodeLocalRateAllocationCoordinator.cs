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
    /// <para>Uses node-local authenticated session counts only; production Redis coordinators observe cluster-wide counts.</para>
    /// </remarks>
    /// <param name="sessionDatabase">Node-local session rows.</param>
    /// <param name="options">Refresh cadence options.</param>
    /// <param name="logger">Logger for material rate-change diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when any constructor argument is null.</exception>
    public sealed partial class NodeLocalRateAllocationCoordinator(
        ISessionDatabase sessionDatabase,
        IOptionsMonitor<NntpRateAllocationOptions> options,
        ILogger<NodeLocalRateAllocationCoordinator> logger) : INntpRateAllocationCoordinator
    {
        /// <summary>
        /// Node-local session database supplying authenticated session counts for fair-share division.
        /// </summary>
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));

        /// <summary>
        /// Monitored rate-allocation options controlling refresh cadence and material-change ratio.
        /// </summary>
        private readonly IOptionsMonitor<NntpRateAllocationOptions> _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>
        /// Logger for material per-session cap changes.
        /// </summary>
        private readonly ILogger<NodeLocalRateAllocationCoordinator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Per-account cached cap and next refresh instant for Option A refresh cadence.
        /// </summary>
        private readonly Dictionary<string, (long Cap, DateTimeOffset NextRefresh)> _refreshState =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Serializes refresh-state mutations across concurrent fair-share requests.
        /// </summary>
        private readonly object _refreshLock = new();

        /// <summary>
        /// Returns the per-session send cap for an account, respecting internal refresh cadence.
        /// </summary>
        /// <param name="policy">Authenticated session policy.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Bytes per second for this TCP session's outbound shaper; 0 disables shaping.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
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
