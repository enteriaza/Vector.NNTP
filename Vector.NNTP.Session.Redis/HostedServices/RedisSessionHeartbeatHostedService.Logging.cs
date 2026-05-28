// <copyright file="RedisSessionHeartbeatHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionHeartbeatHostedService"/>.</summary>
    public sealed partial class RedisSessionHeartbeatHostedService
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Heartbeat failed SessionId={SessionId} AccountKey={AccountKey}")]
        private partial void LogWarningHeartbeatFailed(string sessionId, string accountKey, Exception ex);
    }
}
