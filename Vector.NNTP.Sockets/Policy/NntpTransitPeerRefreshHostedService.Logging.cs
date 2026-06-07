// <copyright file="NntpTransitPeerRefreshHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>LoggerMessage definitions for <see cref="NntpTransitPeerRefreshHostedService"/>.</summary>
    public sealed partial class NntpTransitPeerRefreshHostedService
    {
        /// <summary>
        /// Logs warning when DNS snapshot rebuild fails and the previous matcher snapshot is retained.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="reason">Human-readable failure reason from snapshot rebuild.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Transit peer DNS refresh failed; retaining previous snapshot: {Reason}")]
        private static partial void LogRefreshRetainedPrevious(ILogger logger, string reason);

        /// <summary>
        /// Logs warning when Redis capacity reconciliation fails for a transit peer.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="exception">Exception raised by the coordinator reconcile call.</param>
        /// <param name="peerId">Transit peer identifier that failed reconciliation.</param>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "Transit peer capacity reconcile failed for peer {PeerId}")]
        private static partial void LogCapacityReconcileFailed(ILogger logger, Exception exception, string peerId);
    }
}
