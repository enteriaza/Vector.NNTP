// <copyright file="RedisSessionHeartbeatHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionHeartbeatHostedService"/>.</summary>
    public sealed partial class RedisSessionHeartbeatHostedService
    {
        /// <summary>
        /// Log a warning when a heartbeat fails.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="sessionId">The ID of the session that failed the heartbeat.</param>
        /// <param name="accountKey">The account key of the session that failed the heartbeat.</param>
        /// <param name="ex">The exception that occurred.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Heartbeat failed SessionId={SessionId} AccountKey={AccountKey}")]
        private static partial void LogWarningHeartbeatFailed(ILogger logger, Exception ex, string sessionId, string accountKey);

        /// <summary>Logs a failed transit peer lease refresh.</summary>
        /// <param name="logger">Logger.</param>
        /// <param name="ex">Exception.</param>
        /// <param name="sessionId">Session id.</param>
        /// <param name="peerId">Peer id.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Transit peer heartbeat failed SessionId={SessionId} PeerId={PeerId}")]
        private static partial void LogWarningTransitPeerHeartbeatFailed(
            ILogger logger,
            Exception ex,
            string sessionId,
            string peerId);
    }
}
