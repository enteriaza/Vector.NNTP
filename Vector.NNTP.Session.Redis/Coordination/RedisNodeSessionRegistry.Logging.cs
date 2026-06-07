// <copyright file="RedisNodeSessionRegistry.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>Source-generated logging for <see cref="RedisNodeSessionRegistry"/>.</summary>
    public sealed partial class RedisNodeSessionRegistry
    {
        /// <summary>
        /// Logs warning when purge stops because the iteration bound was reached with sessions still indexed.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="node">Stable node identity being purged.</param>
        /// <param name="iterations">Number of purge iterations completed.</param>
        /// <param name="remainingSessions">Session ids still present in the node index.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Node purge terminated due to iteration limit. Node={Node} Iterations={Iterations} RemainingSessions={RemainingSessions}")]
        private static partial void LogWarningPurgeIterationLimit(
            ILogger logger,
            string node,
            int iterations,
            long remainingSessions);

        /// <summary>
        /// Logs warning when releasing a session during purge fails and metadata cleanup is attempted.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="exception">Exception raised by the release coordinator.</param>
        /// <param name="sessionId">Session identifier that failed release.</param>
        /// <param name="node">Node identity from metadata or the purge target.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to release session during node purge SessionId={SessionId} Node={Node}")]
        private static partial void LogWarningReleaseDuringPurgeFailed(
            ILogger logger,
            Exception exception,
            string sessionId,
            string node);
    }
}
