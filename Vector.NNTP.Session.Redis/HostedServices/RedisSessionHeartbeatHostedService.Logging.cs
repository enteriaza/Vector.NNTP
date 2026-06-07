// <copyright file="RedisSessionHeartbeatHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionHeartbeatHostedService"/>.</summary>
    public sealed partial class RedisSessionHeartbeatHostedService
    {
        /// <summary>
        /// Logs warning when an authenticated session lease heartbeat fails; the loop continues for other sessions.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="ex">Exception raised by the heartbeat script or Redis call.</param>
        /// <param name="sessionId">Session identifier that failed refresh.</param>
        /// <param name="accountKey">Normalized account key for the session.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Heartbeat failed SessionId={SessionId} AccountKey={AccountKey}")]
        private static partial void LogWarningHeartbeatFailed(ILogger logger, Exception ex, string sessionId, string accountKey);

        /// <summary>
        /// Logs warning when a transit peer lease refresh fails; the loop continues for other sessions.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="ex">Exception raised by the refresh coordinator or Redis call.</param>
        /// <param name="sessionId">Session identifier that failed refresh.</param>
        /// <param name="peerId">Transit peer identifier for the session.</param>
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
