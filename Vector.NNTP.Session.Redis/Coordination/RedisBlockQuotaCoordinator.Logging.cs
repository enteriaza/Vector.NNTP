// <copyright file="RedisBlockQuotaCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisBlockQuotaCoordinator"/>.</summary>
    public sealed partial class RedisBlockQuotaCoordinator
    {
        /// <summary>
        /// Log a debug message when the block quota is decremented.
        /// </summary>
        /// <param name="accountKey">The account key of the session.</param>
        /// <param name="commandBytes">The number of command bytes.</param>
        /// <param name="remainingQuotaBytes">The remaining quota bytes.</param>
        [LoggerMessage(
            EventName = "QuotaDecremented",
            Level = LogLevel.Debug,
            Message = "Block quota decremented AccountKey={AccountKey} CommandBytes={CommandBytes} RemainingQuotaBytes={RemainingQuotaBytes}")]
        private partial void LogDebugQuotaDecremented(string accountKey, long commandBytes, long remainingQuotaBytes);
    }
}
