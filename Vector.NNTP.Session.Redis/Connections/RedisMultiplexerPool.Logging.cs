// <copyright file="RedisMultiplexerPool.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Source-generated logging for <see cref="RedisMultiplexerPool"/>.
    /// </summary>
    public sealed partial class RedisMultiplexerPool
    {
        /// <summary>
        /// Log a multiplexer added message.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="connectionId">The connection ID.</param>
        /// <param name="poolSize">The pool size.</param>
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Redis multiplexer added ConnectionId={ConnectionId} PoolSize={PoolSize}.")]
        private static partial void LogMultiplexerAdded(ILogger logger, Guid connectionId, int poolSize);

        /// <summary>
        /// Log a multiplexer connect failed message.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="hostIndex">The host index.</param>
        /// <param name="exception">The exception.</param>
        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Redis multiplexer connect failed HostIndex={HostIndex}.")]
        private static partial void LogMultiplexerConnectFailed(ILogger logger, int hostIndex, Exception exception);
    }
}
