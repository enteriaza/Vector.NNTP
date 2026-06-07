// <copyright file="RedisSessionCountCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionCountCoordinator"/>.</summary>
    public sealed partial class RedisSessionCountCoordinator
    {
        /// <summary>
        /// Log a debug message when the session count changes.
        /// </summary>
        /// <param name="logger">Target logger for the structured event.</param>
        /// <param name="accountKey">The account key of the session.</param>
        /// <param name="newCount">The new session count.</param>
        [LoggerMessage(
            EventName = "SessionCountChanged",
            Level = LogLevel.Debug,
            Message = "Session count read AccountKey={AccountKey} NewCount={NewCount} Source=Redis")]
        private static partial void LogDebugSessionCountChanged(ILogger logger, string accountKey, long newCount);
    }
}
