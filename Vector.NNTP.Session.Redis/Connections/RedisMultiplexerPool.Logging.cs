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
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Redis multiplexer added ConnectionId={ConnectionId} PoolSize={PoolSize}.")]
        private static partial void LogMultiplexerAdded(ILogger logger, Guid connectionId, int poolSize);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Redis multiplexer connect failed HostIndex={HostIndex}.")]
        private static partial void LogMultiplexerConnectFailed(ILogger logger, int hostIndex, Exception exception);
    }
}
