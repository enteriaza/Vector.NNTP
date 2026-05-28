// <copyright file="RedisMultiplexerFactory.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Source-generated logging for <see cref="RedisMultiplexerFactory"/>.
    /// </summary>
    public sealed partial class RedisMultiplexerFactory
    {
        /// <summary>
        /// Log a connecting message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="hostCount">The host count.</param>
        /// <param name="port">The port.</param>
        /// <param name="retry">The retry.</param>
        /// <param name="timeoutSeconds">The timeout seconds.</param>
        [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Connecting Redis multiplexer HostCount={HostCount} Port={Port} Retry={Retry} TimeoutSeconds={TimeoutSeconds}.")]
        private static partial void LogConnecting(ILogger logger, int hostCount, int port, int retry, int timeoutSeconds);

        /// <summary>
        /// Log a connected message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="hostCount">The host count.</param>
        /// <param name="port">The port.</param>
        [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Redis multiplexer connected HostCount={HostCount} Port={Port}.")]
        private static partial void LogConnected(ILogger logger, int hostCount, int port);
    }
}
