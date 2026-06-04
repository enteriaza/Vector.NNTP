// <copyright file="NntpTransitPeerRefreshHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Policy
{
    public sealed partial class NntpTransitPeerRefreshHostedService
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Transit peer DNS refresh failed; retaining previous snapshot: {Reason}")]
        private static partial void LogRefreshRetainedPrevious(ILogger logger, string reason);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "Transit peer capacity reconcile failed for peer {PeerId}")]
        private static partial void LogCapacityReconcileFailed(ILogger logger, Exception exception, string peerId);
    }
}
