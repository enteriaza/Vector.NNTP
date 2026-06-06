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
        /// <summary>
        /// Log a session registered message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="connectionPrefix">Bracketed client endpoint prefix for log correlation.</param>
        /// <param name="sessionId">The session ID.</param>
        [LoggerMessage(EventName = "SessionRegistered", Level = LogLevel.Debug, Message = "{ConnectionPrefix} Connection session registered SessionId={SessionId}")]
        public static partial void SessionRegistered(ILogger logger, string connectionPrefix, string sessionId);

        /// <summary>
        /// Log a session registered duplicate message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="connectionPrefix">Bracketed client endpoint prefix for log correlation.</param>
        /// <param name="sessionId">The session ID.</param>
        [LoggerMessage(EventName = "SessionRegisteredDuplicate", Level = LogLevel.Warning, Message = "{ConnectionPrefix} Duplicate session insert SessionId={SessionId}")]
        public static partial void SessionRegisteredDuplicate(ILogger logger, string connectionPrefix, string sessionId);

        /// <summary>
        /// Log a session removed message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="reason">The reason.</param>
        /// <returns><see langword="true"/> when removed.</returns>
        /// <returns><see langword="true"/> when removed.</returns>
        [LoggerMessage(EventName = "SessionRemoved", Level = LogLevel.Debug, Message = "Connection session removed SessionId={SessionId} Reason={Reason}")]
        public static partial void SessionRemoved(ILogger logger, string sessionId, string reason);

        /// <summary>
        /// Log a session authenticating message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="phase">The phase.</param>
        /// <returns><see langword="true"/> when authenticating.</returns>
        [LoggerMessage(EventName = "SessionAuthenticating", Level = LogLevel.Debug, Message = "Session authenticating SessionId={SessionId} Phase={Phase}")]
        public static partial void SessionAuthenticating(ILogger logger, string sessionId, string phase);

        /// <summary>
        /// Log a session authenticated message.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="username">The username.</param>
        /// <param name="accountKey">The account key.</param>
        [LoggerMessage(EventName = "SessionAuthenticated", Level = LogLevel.Information, Message = "Session authenticated SessionId={SessionId} Username={Username} AccountKey={AccountKey}")]
        public static partial void SessionAuthenticated(ILogger logger, string sessionId, string username, string accountKey);
    }
}
