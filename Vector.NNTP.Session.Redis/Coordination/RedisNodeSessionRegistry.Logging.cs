// <copyright file="RedisNodeSessionRegistry.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>Source-generated logging for <see cref="RedisNodeSessionRegistry"/>.</summary>
    public sealed partial class RedisNodeSessionRegistry
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Node purge terminated due to iteration limit. Node={Node} Iterations={Iterations} RemainingSessions={RemainingSessions}")]
        private static partial void LogWarningPurgeIterationLimit(
            ILogger logger,
            string node,
            int iterations,
            long remainingSessions);

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
