// <copyright file="RedisMultiplexerPoolSupervisor.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Source-generated logging for <see cref="RedisMultiplexerPoolSupervisor"/>.
    /// </summary>
    public sealed partial class RedisMultiplexerPoolSupervisor
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Redis multiplexer pool supervisor started PoolSize={PoolSize}.")]
        private static partial void LogSupervisorStarted(ILogger logger, int poolSize);
    }
}
