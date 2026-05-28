// <copyright file="RedisRateAllocationCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisRateAllocationCoordinator"/>.</summary>
    public sealed partial class RedisRateAllocationCoordinator
    {
        /// <summary>
        /// Log an information message when the account rate is rebalanced.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountKey">The account key.</param>
        /// <param name="observedSessionCount">The observed session count.</param>
        /// <param name="perSessionBytesPerSecond">The per session bytes per second.</param>
        /// <param name="accountRateBytesPerSecond">The account rate bytes per second.</param>
        [LoggerMessage(
            EventName = "AccountRateRebalanced",
            Level = LogLevel.Information,
            Message = "Fair-share updated AccountKey={AccountKey} ObservedSessionCount={ObservedSessionCount} PerSessionBytesPerSecond={PerSessionBytesPerSecond} AccountRateBytesPerSecond={AccountRateBytesPerSecond}")]
        private static partial void LogInformationAccountRateRebalanced(
            ILogger logger,
            string accountKey,
            long observedSessionCount,
            long perSessionBytesPerSecond,
            long accountRateBytesPerSecond);
    }
}
