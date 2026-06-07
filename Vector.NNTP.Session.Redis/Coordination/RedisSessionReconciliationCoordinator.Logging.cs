// <copyright file="RedisSessionReconciliationCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionReconciliationCoordinator"/>.</summary>
    public sealed partial class RedisSessionReconciliationCoordinator
    {
        /// <summary>
        /// Logs debug when a bounded reconciliation pass starts for an account.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="accountKey">Normalized account key being reconciled.</param>
        [LoggerMessage(Level = LogLevel.Debug, Message = "Redis reconciliation started AccountKey={AccountKey}")]
        private static partial void LogDebugRedisReconciliationStarted(ILogger logger, string accountKey);

        /// <summary>
        /// Logs information when reconciliation completes with the post-pass session count.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="accountKey">Normalized account key that was reconciled.</param>
        /// <param name="sessionsAfter">Live session count recorded in Redis after the pass.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Redis reconciliation completed AccountKey={AccountKey} SessionsAfter={SessionsAfter}")]
        private static partial void LogInformationRedisReconciliationCompleted(ILogger logger, string accountKey, long sessionsAfter);

        /// <summary>
        /// Logs information when orphan session anchors are deleted during reconciliation.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="accountKey">Normalized account key whose orphans were purged.</param>
        /// <param name="purgedCount">Number of orphan session anchors removed.</param>
        /// <param name="liveSessionCount">Number of live sessions on this node used for the live set.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Redis orphan session anchors purged AccountKey={AccountKey} PurgedCount={PurgedCount} LiveSessionCount={LiveSessionCount}")]
        private static partial void LogInformationRedisOrphanAnchorsPurged(ILogger logger, string accountKey, long purgedCount, int liveSessionCount);

        /// <summary>
        /// Logs warning when a reconciliation pass fails for an account.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="ex">Exception raised by the reconciliation script or Redis call.</param>
        /// <param name="accountKey">Normalized account key that failed reconciliation.</param>
        [LoggerMessage(Level = LogLevel.Warning, Message = "Redis reconciliation failed AccountKey={AccountKey}")]
        private static partial void LogWarningRedisReconciliationFailed(ILogger logger, Exception ex, string accountKey);
    }
}
