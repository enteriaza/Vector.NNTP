// <copyright file="NntpQuotaEnforcer.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Session.Quota
{
    /// <summary>
    /// Applies byte quota decrement and rate allocation refresh after NNTP commands.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpQuotaEnforcer"/> class.
    /// </remarks>
    /// <param name="blockQuotaCoordinator">Block quota coordinator.</param>
    /// <param name="rateAllocationCoordinator">Rate allocation coordinator.</param>
    /// <param name="logger">Logger.</param>
    public sealed partial class NntpQuotaEnforcer(
        INntpBlockQuotaCoordinator blockQuotaCoordinator,
        INntpRateAllocationCoordinator rateAllocationCoordinator,
        ILogger<NntpQuotaEnforcer> logger)
    {
        private const string AcctStopReasonBlockQuota = "block_quota";
        private const string AcctStopReasonBlockQuotaError = "block_quota_error";

        private readonly INntpBlockQuotaCoordinator _blockQuotaCoordinator = blockQuotaCoordinator ?? throw new ArgumentNullException(nameof(blockQuotaCoordinator));
        private readonly INntpRateAllocationCoordinator _rateAllocationCoordinator = rateAllocationCoordinator ?? throw new ArgumentNullException(nameof(rateAllocationCoordinator));
        private readonly ILogger<NntpQuotaEnforcer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Decrements block quota after a command when the account is byte-limited.
        /// </summary>
        /// <param name="policy">Authenticated policy.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="commandBytes">Bytes attributed to the command.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Enforcement outcome for the runner.</returns>
        public async ValueTask<QuotaEnforcementResult> ApplyBlockQuotaAfterCommandAsync(
            NntpSessionPolicy policy,
            string sessionId,
            long commandBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (policy.AccountType != NntpAccountType.ByteLimited || policy.ByteLimit <= 0 || commandBytes <= 0)
            {
                return new QuotaEnforcementResult(false, string.Empty);
            }

            try
            {
                _ = await _blockQuotaCoordinator.TryInitializeQuotaAsync(policy.AccountKey, policy.ByteLimit, cancellationToken).ConfigureAwait(false);
                long remaining = await _blockQuotaCoordinator.DecrementAsync(policy.AccountKey, commandBytes, cancellationToken).ConfigureAwait(false);
                NntpQuotaEnforcerLog.QuotaDecremented(_logger, policy.AccountKey, commandBytes, remaining);

                if (remaining <= 0)
                {
                    NntpQuotaEnforcerLog.QuotaExceeded(_logger, policy.AccountKey, sessionId, policy.Username);
                    NntpQuotaEnforcerLog.ForcedDeauth(_logger, sessionId, policy.Username, AcctStopReasonBlockQuota);
                    return new QuotaEnforcementResult(true, AcctStopReasonBlockQuota);
                }
            }
            catch (Exception ex)
            {
                NntpQuotaEnforcerLog.QuotaEnforcementFailed(_logger, ex, policy.AccountKey, sessionId);
                NntpQuotaEnforcerLog.ForcedDeauth(_logger, sessionId, policy.Username, AcctStopReasonBlockQuotaError);
                return new QuotaEnforcementResult(true, AcctStopReasonBlockQuotaError);
            }

            return new QuotaEnforcementResult(false, string.Empty);
        }

        /// <summary>
        /// Refreshes fair-share outbound rate for rate-limited accounts.
        /// </summary>
        /// <param name="policy">Authenticated policy.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Updated per-session bytes per second (0 = unlimited).</returns>
        public async ValueTask<long> RefreshRateLimitAsync(NntpSessionPolicy policy, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            return policy.AccountType != NntpAccountType.RateLimited || policy.RateBytesPerSecond <= 0
                ? 0
                : await _rateAllocationCoordinator.GetPerSessionSendRateBytesPerSecondAsync(policy, cancellationToken).ConfigureAwait(false);
        }
    }
}
