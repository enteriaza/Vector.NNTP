// <copyright file="RedisSessionLeaseRefresher.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionLeaseRefresher"/>.</summary>
    public sealed partial class RedisSessionLeaseRefresher
    {
        /// <summary>
        /// Logs trace when a session lease heartbeat succeeds.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="sessionId">Session identifier whose lease was refreshed.</param>
        /// <param name="accountKey">Normalized account key for the session.</param>
        /// <param name="ttlSeconds">Lease TTL seconds applied by the heartbeat script.</param>
        [LoggerMessage(
            EventName = "RedisLeaseRefreshed",
            Level = LogLevel.Trace,
            Message = "Redis lease refreshed SessionId={SessionId} AccountKey={AccountKey} TtlSeconds={TtlSeconds}")]
        private static partial void LogTraceRedisLeaseRefreshed(ILogger logger, string sessionId, string accountKey, int ttlSeconds);

        /// <summary>
        /// Logs warning when a session lease heartbeat fails before the exception is rethrown.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="ex">Exception raised by the heartbeat script or Redis call.</param>
        /// <param name="accountKey">Normalized account key for the session.</param>
        /// <param name="sessionId">Session identifier that failed refresh.</param>
        [LoggerMessage(
            EventName = "RedisLeaseExpired",
            Level = LogLevel.Warning,
            Message = "Redis lease refresh failed AccountKey={AccountKey} SessionId={SessionId}")]
        private static partial void LogWarningRedisLeaseRefreshFailed(ILogger logger, Exception ex, string accountKey, string sessionId);

        /// <summary>
        /// Logs warning when a heartbeat Redis call exceeds the configured slow threshold.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="operation">Logical operation name (for example <c>session-heartbeat</c>).</param>
        /// <param name="elapsedMs">Measured elapsed time in milliseconds.</param>
        [LoggerMessage(
            EventName = "RedisOperationSlow",
            Level = LogLevel.Warning,
            Message = "Slow Redis call Operation={Operation} ElapsedMs={ElapsedMs}")]
        private static partial void LogWarningRedisOperationSlow(ILogger logger, string operation, double elapsedMs);
    }
}
