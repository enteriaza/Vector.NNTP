// <copyright file="InMemorySessionDatabase.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Session.Database
{
    /// <summary>
    /// Source-generated logging for <see cref="InMemorySessionDatabase"/>.
    /// </summary>
    internal static partial class InMemorySessionDatabaseLog
    {
        [LoggerMessage(EventName = "SessionRegistered", Level = LogLevel.Debug, Message = "Connection session registered SessionId={SessionId} ClientIp={ClientIp}")]
        public static partial void SessionRegistered(ILogger logger, string sessionId, string clientIp);

        [LoggerMessage(EventName = "SessionRegisteredDuplicate", Level = LogLevel.Warning, Message = "Duplicate session insert SessionId={SessionId}")]
        public static partial void SessionRegisteredDuplicate(ILogger logger, string sessionId);

        [LoggerMessage(EventName = "SessionRemoved", Level = LogLevel.Debug, Message = "Connection session removed SessionId={SessionId} Reason={Reason}")]
        public static partial void SessionRemoved(ILogger logger, string sessionId, string reason);

        [LoggerMessage(EventName = "SessionAuthenticating", Level = LogLevel.Debug, Message = "Session authenticating SessionId={SessionId} Phase={Phase}")]
        public static partial void SessionAuthenticating(ILogger logger, string sessionId, string phase);

        [LoggerMessage(EventName = "SessionAuthenticated", Level = LogLevel.Information, Message = "Session authenticated SessionId={SessionId} Username={Username} AccountKey={AccountKey}")]
        public static partial void SessionAuthenticated(ILogger logger, string sessionId, string username, string accountKey);
    }
}
