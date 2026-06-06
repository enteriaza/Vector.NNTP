// <copyright file="HistoryRedisStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 200-219.

namespace Vector.NNTP.HistoryDB.Redis
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="HistoryRedisStore"/>.
    /// </summary>
    /// <remarks>
    /// Implementations bind to the primary-constructor <c>logger</c> parameter from <see cref="HistoryRedisStore"/>.
    /// </remarks>
    internal sealed partial class HistoryRedisStore
    {
        /// <summary>
        /// Threshold in milliseconds above which Redis Lua calls are logged as slow.
        /// </summary>
        private const long SlowRedisThresholdMs = 50;

        /// <summary>Logs a slow Redis Lua script invocation.</summary>
        /// <param name="operation">Operation name.</param>
        /// <param name="elapsedMs">Elapsed milliseconds.</param>
        [LoggerMessage(EventId = 200, Level = LogLevel.Warning,
            Message = "HistoryDB slow Redis {Operation} completed in {ElapsedMs} ms.")]
        private partial void LogSlowRedisCall(string operation, long elapsedMs);
    }
}
