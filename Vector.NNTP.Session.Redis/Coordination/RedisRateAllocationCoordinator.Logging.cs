// <copyright file="RedisRateAllocationCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisRateAllocationCoordinator"/>.</summary>
    public sealed partial class RedisRateAllocationCoordinator
    {
        [LoggerMessage(
            EventName = "AccountRateRebalanced",
            Level = LogLevel.Information,
            Message = "Fair-share updated AccountKey={AccountKey} ObservedSessionCount={ObservedSessionCount} PerSessionBytesPerSecond={PerSessionBytesPerSecond} AccountRateBytesPerSecond={AccountRateBytesPerSecond}")]
        private partial void LogInformationAccountRateRebalanced(
            string accountKey,
            long observedSessionCount,
            long perSessionBytesPerSecond,
            long accountRateBytesPerSecond);
    }
}
