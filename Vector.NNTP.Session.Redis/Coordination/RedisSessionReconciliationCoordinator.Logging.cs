// <copyright file="RedisSessionReconciliationCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionReconciliationCoordinator"/>.</summary>
    public sealed partial class RedisSessionReconciliationCoordinator
    {
        /// <summary>
        /// Log a debug message when Redis reconciliation starts.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountKey">The account key of the session that failed the heartbeat.</param>
        [LoggerMessage(Level = LogLevel.Debug, Message = "Redis reconciliation started AccountKey={AccountKey}")]
        private static partial void LogDebugRedisReconciliationStarted(ILogger logger, string accountKey);

        /// <summary>
        /// Log an information message when Redis reconciliation completes.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountKey">The account key of the session that failed the heartbeat.</param>
        /// <param name="sessionsAfter">The number of sessions after the reconciliation.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Redis reconciliation completed AccountKey={AccountKey} SessionsAfter={SessionsAfter}")]
        private static partial void LogInformationRedisReconciliationCompleted(ILogger logger, string accountKey, long sessionsAfter);

        /// <summary>
        /// Log an information message when Redis orphan session anchors are purged.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountKey">The account key of the session that failed the heartbeat.</param>
        /// <param name="purgedCount">The number of orphan session anchors purged.</param>
        /// <param name="liveSessionCount">The number of live sessions.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Redis orphan session anchors purged AccountKey={AccountKey} PurgedCount={PurgedCount} LiveSessionCount={LiveSessionCount}")]
        private static partial void LogInformationRedisOrphanAnchorsPurged(ILogger logger, string accountKey, long purgedCount, int liveSessionCount);

        /// <summary>
        /// Log a warning message when Redis reconciliation fails.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="accountKey">The account key of the session that failed the heartbeat.</param>
        [LoggerMessage(Level = LogLevel.Warning, Message = "Redis reconciliation failed AccountKey={AccountKey}")]
        private static partial void LogWarningRedisReconciliationFailed(ILogger logger, Exception ex, string accountKey);
    }
}
