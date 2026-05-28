// <copyright file="RedisSessionLeaseRefresher.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionLeaseRefresher"/>.</summary>
    public sealed partial class RedisSessionLeaseRefresher
    {
        /// <summary>
        /// Log a trace message when a Redis lease is refreshed.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="sessionId">The ID of the session that was refreshed.</param>
        /// <param name="accountKey">The account key of the session that was refreshed.</param>
        /// <param name="ttlSeconds">The TTL of the session in seconds.</param>
        [LoggerMessage(
            EventName = "RedisLeaseRefreshed",
            Level = LogLevel.Trace,
            Message = "Redis lease refreshed SessionId={SessionId} AccountKey={AccountKey} TtlSeconds={TtlSeconds}")]
        private static partial void LogTraceRedisLeaseRefreshed(ILogger logger, string sessionId, string accountKey, int ttlSeconds);

        /// <summary>
        /// Log a warning message when a Redis lease refresh fails.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountKey">The account key of the session that failed the refresh.</param>
        /// <param name="sessionId">The ID of the session that failed the refresh.</param>
        /// <param name="ex">The exception that occurred.</param>
        [LoggerMessage(
            EventName = "RedisLeaseExpired",
            Level = LogLevel.Warning,
            Message = "Redis lease refresh failed AccountKey={AccountKey} SessionId={SessionId}")]
        private static partial void LogWarningRedisLeaseRefreshFailed(ILogger logger, Exception ex, string accountKey, string sessionId);

        /// <summary>
        /// Log a warning message when a Redis operation is slow.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="operation">The operation that was slow.</param>
        /// <param name="elapsedMs">The elapsed time in milliseconds.</param>
        [LoggerMessage(
            EventName = "RedisOperationSlow",
            Level = LogLevel.Warning,
            Message = "Slow Redis call Operation={Operation} ElapsedMs={ElapsedMs}")]
        private static partial void LogWarningRedisOperationSlow(ILogger logger, string operation, double elapsedMs);
    }
}
