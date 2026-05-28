// <copyright file="RedisSessionCountCoordinator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>LoggerMessage definitions for <see cref="RedisSessionCountCoordinator"/>.</summary>
    public sealed partial class RedisSessionCountCoordinator
    {
        [LoggerMessage(
            EventName = "SessionCountChanged",
            Level = LogLevel.Debug,
            Message = "Session count read AccountKey={AccountKey} NewCount={NewCount} Source=Redis")]
        private partial void LogDebugSessionCountChanged(string accountKey, long newCount);
    }
}
