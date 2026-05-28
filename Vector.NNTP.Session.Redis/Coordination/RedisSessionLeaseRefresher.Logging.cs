// <copyright file="RedisSessionLeaseRefresher.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionLeaseRefresher"/>.</summary>
    public sealed partial class RedisSessionLeaseRefresher
    {
        [LoggerMessage(
            EventName = "RedisLeaseRefreshed",
            Level = LogLevel.Trace,
            Message = "Redis lease refreshed SessionId={SessionId} AccountKey={AccountKey} TtlSeconds={TtlSeconds}")]
        private partial void LogTraceRedisLeaseRefreshed(string sessionId, string accountKey, int ttlSeconds);

        [LoggerMessage(
            EventName = "RedisLeaseExpired",
            Level = LogLevel.Warning,
            Message = "Redis lease refresh failed AccountKey={AccountKey} SessionId={SessionId}")]
        private partial void LogWarningRedisLeaseRefreshFailed(string accountKey, string sessionId, Exception ex);

        [LoggerMessage(
            EventName = "RedisOperationSlow",
            Level = LogLevel.Warning,
            Message = "Slow Redis call Operation={Operation} ElapsedMs={ElapsedMs}")]
        private partial void LogWarningRedisOperationSlow(string operation, double elapsedMs);
    }
}
