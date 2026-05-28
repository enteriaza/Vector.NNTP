// <copyright file="RedisMultiplexerBackgroundScaler.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Source-generated logging for <see cref="RedisMultiplexerBackgroundScaler"/>.
    /// </summary>
    public sealed partial class RedisMultiplexerBackgroundScaler
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Redis multiplexer pool scaled up PoolSize={PoolSize}.")]
        private static partial void LogScaledUp(ILogger logger, int poolSize);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Redis multiplexer scale-up failed.")]
        private static partial void LogScaleError(ILogger logger, Exception exception);
    }
}
