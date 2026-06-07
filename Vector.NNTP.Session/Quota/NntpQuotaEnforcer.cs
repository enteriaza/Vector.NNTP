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
    /// <para>Invoked by the NNTP session runner after commands that carry billable bytes. Block quota failures force
    /// deauthentication with stable reason codes for accounting.</para>
    /// </remarks>
    /// <param name="blockQuotaCoordinator">Block quota coordinator.</param>
    /// <param name="rateAllocationCoordinator">Rate allocation coordinator.</param>
    /// <param name="logger">Logger for quota and deauth events.</param>
    /// <exception cref="ArgumentNullException">Thrown when any constructor argument is null.</exception>
    public sealed partial class NntpQuotaEnforcer(
        INntpBlockQuotaCoordinator blockQuotaCoordinator,
        INntpRateAllocationCoordinator rateAllocationCoordinator,
        ILogger<NntpQuotaEnforcer> logger)
    {
        /// <summary>
        /// Accounting stop reason when byte quota is exhausted.
        /// </summary>
        private const string AcctStopReasonBlockQuota = "block_quota";

        /// <summary>
        /// Accounting stop reason when quota decrement fails unexpectedly.
        /// </summary>
        private const string AcctStopReasonBlockQuotaError = "block_quota_error";

        /// <summary>
        /// Distributed or in-memory block quota coordinator.
        /// </summary>
        private readonly INntpBlockQuotaCoordinator _blockQuotaCoordinator = blockQuotaCoordinator ?? throw new ArgumentNullException(nameof(blockQuotaCoordinator));

        /// <summary>
        /// Fair-share rate coordinator supplying per-session send caps.
        /// </summary>
        private readonly INntpRateAllocationCoordinator _rateAllocationCoordinator = rateAllocationCoordinator ?? throw new ArgumentNullException(nameof(rateAllocationCoordinator));

        /// <summary>
        /// Logger for quota decrement, exhaustion, and forced deauth events.
        /// </summary>
        private readonly ILogger<NntpQuotaEnforcer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Decrements block quota after a command when the account is byte-limited.
        /// </summary>
        /// <param name="policy">Authenticated policy.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="commandBytes">Bytes attributed to the command.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Enforcement outcome for the runner.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
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
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
        public async ValueTask<long> RefreshRateLimitAsync(NntpSessionPolicy policy, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            return policy.AccountType != NntpAccountType.RateLimited || policy.RateBytesPerSecond <= 0
                ? 0
                : await _rateAllocationCoordinator.GetPerSessionSendRateBytesPerSecondAsync(policy, cancellationToken).ConfigureAwait(false);
        }
    }
}
