// <copyright file="NodeLocalRateAllocationCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Source-generated logging for <see cref="NodeLocalRateAllocationCoordinator"/>.
    /// </summary>
    public sealed partial class NodeLocalRateAllocationCoordinator
    {
        /// <summary>
        /// Logs a material fair-share cap update for an account.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="observedSessionCount">Authenticated session count used as divisor.</param>
        /// <param name="perSessionBytesPerSecond">New per-session send cap.</param>
        /// <param name="accountRateBytesPerSecond">Aggregate account ceiling.</param>
        [LoggerMessage(
            EventName = "AccountRateRebalanced",
            Level = LogLevel.Information,
            Message = "Fair-share updated AccountKey={AccountKey} ObservedSessionCount={ObservedSessionCount} PerSessionBytesPerSecond={PerSessionBytesPerSecond} AccountRateBytesPerSecond={AccountRateBytesPerSecond}")]
        private partial void LogInformationAccountRateRebalanced(
            string accountKey,
            long observedSessionCount,
            long perSessionBytesPerSecond,
            long accountRateBytesPerSecond);

        /// <summary>
        /// Logs when refresh cadence suppresses a fair-share recompute.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="nextRefreshInMs">Milliseconds until the next allowed refresh.</param>
        [LoggerMessage(
            EventName = "AccountRateRebalanceSkipped",
            Level = LogLevel.Debug,
            Message = "Fair-share refresh skipped AccountKey={AccountKey} NextRefreshInMs={NextRefreshInMs}")]
        private partial void LogDebugAccountRateRebalanceSkipped(string accountKey, double nextRefreshInMs);
    }
}
