// <copyright file="RedisBlockQuotaCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisBlockQuotaCoordinator"/>.</summary>
    public sealed partial class RedisBlockQuotaCoordinator
    {
        [LoggerMessage(
            EventName = "QuotaDecremented",
            Level = LogLevel.Debug,
            Message = "Block quota decremented AccountKey={AccountKey} CommandBytes={CommandBytes} RemainingQuotaBytes={RemainingQuotaBytes}")]
        private partial void LogDebugQuotaDecremented(string accountKey, long commandBytes, long remainingQuotaBytes);
    }
}
