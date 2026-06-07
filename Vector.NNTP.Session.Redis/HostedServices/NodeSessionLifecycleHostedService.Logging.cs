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
        /// <summary>
        /// Logs information when startup purge completes for this node.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="node">Stable node identity purged.</param>
        /// <param name="auth">Authenticated leases released during purge.</param>
        /// <param name="transit">Transit peer leases released during purge.</param>
        /// <param name="ms">Purge wall-clock duration in milliseconds.</param>
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

        /// <summary>
        /// Logs information when shutdown purge completes after survivor release.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="node">Stable node identity purged.</param>
        /// <param name="auth">Authenticated leases released during purge.</param>
        /// <param name="transit">Transit peer leases released during purge.</param>
        /// <param name="ms">Purge wall-clock duration in milliseconds.</param>
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

        /// <summary>
        /// Logs warning when releasing a survivor authenticated session fails during shutdown.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="exception">Exception raised by the auth release coordinator.</param>
        /// <param name="sessionId">Survivor session identifier.</param>
        /// <param name="accountKey">Normalized account key for the survivor session.</param>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "Failed to release survivor auth session {SessionId} account {AccountKey} during shutdown.")]
        private static partial void LogWarningSurvivorAuthReleaseFailed(
            ILogger logger,
            Exception exception,
            string sessionId,
            string accountKey);

        /// <summary>
        /// Logs warning when releasing a survivor transit session fails during shutdown.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="exception">Exception raised by the transit release coordinator.</param>
        /// <param name="sessionId">Survivor session identifier.</param>
        /// <param name="peerId">Transit peer identifier for the survivor session.</param>
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
