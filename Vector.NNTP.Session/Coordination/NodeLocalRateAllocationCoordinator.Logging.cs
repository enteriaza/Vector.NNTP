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
        /// Log an information message when the account rate is rebalanced.
        /// </summary>
        /// <param name="accountKey">The account key.</param>
        /// <param name="observedSessionCount">The observed session count.</param>
        /// <param name="perSessionBytesPerSecond">The per session bytes per second.</param>
        /// <param name="accountRateBytesPerSecond">The account rate bytes per second.</param>
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
        /// Log a debug message when the account rate rebalance is skipped.
        /// </summary>
        /// <param name="accountKey">The account key.</param>
        /// <param name="nextRefreshInMs">The next refresh in milliseconds.</param>
        [LoggerMessage(
            EventName = "AccountRateRebalanceSkipped",
            Level = LogLevel.Debug,
            Message = "Fair-share refresh skipped AccountKey={AccountKey} NextRefreshInMs={NextRefreshInMs}")]
        private partial void LogDebugAccountRateRebalanceSkipped(string accountKey, double nextRefreshInMs);
    }
}
