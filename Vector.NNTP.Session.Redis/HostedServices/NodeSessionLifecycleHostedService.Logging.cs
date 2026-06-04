// <copyright file="NodeSessionLifecycleHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>
    /// Source-generated logging for <see cref="NodeSessionLifecycleHostedService"/>.
    /// </summary>
    internal sealed partial class NodeSessionLifecycleHostedService
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Node session purge completed Node={Node} AuthLeases={Auth} TransitLeases={Transit} DurationMs={Ms}")]
        private static partial void LogInformationStartupPurgeCompleted(
            ILogger logger,
            string node,
            long auth,
            long transit,
            double ms);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Node session shutdown purge completed Node={Node} AuthLeases={Auth} TransitLeases={Transit} DurationMs={Ms}")]
        private static partial void LogInformationShutdownPurgeCompleted(
            ILogger logger,
            string node,
            long auth,
            long transit,
            double ms);

        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "Failed to release survivor auth session {SessionId} account {AccountKey} during shutdown.")]
        private static partial void LogWarningSurvivorAuthReleaseFailed(
            ILogger logger,
            Exception exception,
            string sessionId,
            string accountKey);

        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Warning,
            Message = "Failed to release survivor transit session {SessionId} peer {PeerId} during shutdown.")]
        private static partial void LogWarningSurvivorTransitReleaseFailed(
            ILogger logger,
            Exception exception,
            string sessionId,
            string peerId);
    }
}
