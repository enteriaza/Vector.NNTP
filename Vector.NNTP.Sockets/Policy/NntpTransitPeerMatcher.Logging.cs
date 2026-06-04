// <copyright file="NntpTransitPeerMatcher.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>
    /// Source-generated logging for <see cref="NntpTransitPeerMatcher"/>.
    /// </summary>
    public sealed partial class NntpTransitPeerMatcher
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Transit peer snapshot rebuilt: {SourceCount} sources across {PeerCount} peers")]
        private static partial void LogSnapshotRebuilt(ILogger logger, int sourceCount, int peerCount);
    }
}
