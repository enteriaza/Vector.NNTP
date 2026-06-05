// <copyright file="RedisTransitPeerCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>Source-generated logging for <see cref="RedisTransitPeerCoordinator"/>.</summary>
    public sealed partial class RedisTransitPeerCoordinator
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Transit peer acquire failed PeerId={PeerId} SessionId={SessionId}")]
        private static partial void LogWarningTransitPeerAcquireFailed(
            ILogger logger,
            Exception exception,
            string peerId,
            string sessionId);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "Transit peer release failed PeerId={PeerId} SessionId={SessionId}")]
        private static partial void LogWarningTransitPeerReleaseFailed(
            ILogger logger,
            Exception exception,
            string peerId,
            string sessionId);

        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "Transit peer lease refresh failed PeerId={PeerId} SessionId={SessionId}")]
        private static partial void LogWarningTransitPeerRefreshFailed(
            ILogger logger,
            Exception exception,
            string peerId,
            string sessionId);

        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Warning,
            Message = "Slow Redis {Operation} ({ElapsedMs:F1} ms)")]
        private static partial void LogWarningRedisOperationSlow(ILogger logger, string operation, double elapsedMs);
    }
}
