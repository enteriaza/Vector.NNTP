// <copyright file="RedisSessionReconciliationCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionReconciliationCoordinator"/>.</summary>
    public sealed partial class RedisSessionReconciliationCoordinator
    {
        [LoggerMessage(
            EventName = "RedisReconciliationStarted",
            Level = LogLevel.Debug,
            Message = "Redis reconciliation started AccountKey={AccountKey}")]
        private partial void LogDebugRedisReconciliationStarted(string accountKey);

        [LoggerMessage(
            EventName = "RedisReconciliationCompleted",
            Level = LogLevel.Information,
            Message = "Redis reconciliation completed AccountKey={AccountKey} SessionsAfter={SessionsAfter}")]
        private partial void LogInformationRedisReconciliationCompleted(string accountKey, long sessionsAfter);

        [LoggerMessage(
            EventName = "RedisOrphanAnchorsPurged",
            Level = LogLevel.Information,
            Message = "Redis orphan session anchors purged AccountKey={AccountKey} PurgedCount={PurgedCount} LiveSessionCount={LiveSessionCount}")]
        private partial void LogInformationRedisOrphanAnchorsPurged(string accountKey, long purgedCount, int liveSessionCount);

        [LoggerMessage(
            EventName = "RedisReconciliationFailed",
            Level = LogLevel.Warning,
            Message = "Redis reconciliation failed AccountKey={AccountKey}")]
        private partial void LogWarningRedisReconciliationFailed(string accountKey, Exception ex);
    }
}
