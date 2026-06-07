// <copyright file="RedisTransitPeerCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>Source-generated logging for <see cref="RedisTransitPeerCoordinator"/>.</summary>
    public sealed partial class RedisTransitPeerCoordinator
    {
        /// <summary>
        /// Logs warning when transit peer acquire fails and returns backend failure to callers.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="exception">Exception raised by the acquire script or Redis call.</param>
        /// <param name="peerId">Transit peer identifier.</param>
        /// <param name="sessionId">Session identifier being admitted.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Transit peer acquire failed PeerId={PeerId} SessionId={SessionId}")]
        private static partial void LogWarningTransitPeerAcquireFailed(
            ILogger logger,
            Exception exception,
            string peerId,
            string sessionId);

        /// <summary>
        /// Logs warning when transit peer release fails before the exception is rethrown.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="exception">Exception raised by the release script or Redis call.</param>
        /// <param name="peerId">Transit peer identifier.</param>
        /// <param name="sessionId">Session identifier being released.</param>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "Transit peer release failed PeerId={PeerId} SessionId={SessionId}")]
        private static partial void LogWarningTransitPeerReleaseFailed(
            ILogger logger,
            Exception exception,
            string peerId,
            string sessionId);

        /// <summary>
        /// Logs warning when transit peer lease refresh fails before the exception is rethrown.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="exception">Exception raised by the refresh script or Redis call.</param>
        /// <param name="peerId">Transit peer identifier.</param>
        /// <param name="sessionId">Session identifier being refreshed.</param>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "Transit peer lease refresh failed PeerId={PeerId} SessionId={SessionId}")]
        private static partial void LogWarningTransitPeerRefreshFailed(
            ILogger logger,
            Exception exception,
            string peerId,
            string sessionId);

        /// <summary>
        /// Logs warning when a transit peer Redis call exceeds the configured slow threshold.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="operation">Logical operation name (for example <c>transit-peer-acquire</c>).</param>
        /// <param name="elapsedMs">Measured elapsed time in milliseconds.</param>
        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Warning,
            Message = "Slow Redis {Operation} ({ElapsedMs:F1} ms)")]
        private static partial void LogWarningRedisOperationSlow(ILogger logger, string operation, double elapsedMs);
    }
}
