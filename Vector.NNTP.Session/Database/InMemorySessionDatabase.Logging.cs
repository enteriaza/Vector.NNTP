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
        /// Logs successful session registration at TCP accept.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="connectionPrefix">Bracketed client endpoint prefix for log correlation.</param>
        /// <param name="sessionId">Registered session identifier.</param>
        [LoggerMessage(EventName = "SessionRegistered", Level = LogLevel.Debug, Message = "{ConnectionPrefix} Connection session registered SessionId={SessionId}")]
        public static partial void SessionRegistered(ILogger logger, string connectionPrefix, string sessionId);

        /// <summary>
        /// Logs a duplicate session insert attempt.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="connectionPrefix">Bracketed client endpoint prefix for log correlation.</param>
        /// <param name="sessionId">Colliding session identifier.</param>
        [LoggerMessage(EventName = "SessionRegisteredDuplicate", Level = LogLevel.Warning, Message = "{ConnectionPrefix} Duplicate session insert SessionId={SessionId}")]
        public static partial void SessionRegisteredDuplicate(ILogger logger, string connectionPrefix, string sessionId);

        /// <summary>
        /// Logs session removal on connection teardown.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="sessionId">Removed session identifier.</param>
        /// <param name="reason">Teardown reason label.</param>
        [LoggerMessage(EventName = "SessionRemoved", Level = LogLevel.Debug, Message = "Connection session removed SessionId={SessionId} Reason={Reason}")]
        public static partial void SessionRemoved(ILogger logger, string sessionId, string reason);

        /// <summary>
        /// Logs transition into an authenticating sub-phase.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="phase">Authenticating phase label.</param>
        [LoggerMessage(EventName = "SessionAuthenticating", Level = LogLevel.Debug, Message = "Session authenticating SessionId={SessionId} Phase={Phase}")]
        public static partial void SessionAuthenticating(ILogger logger, string sessionId, string phase);

        /// <summary>
        /// Logs successful authentication after distributed admission.
        /// </summary>
        /// <param name="logger">Target logger.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="username">Authenticated username.</param>
        /// <param name="accountKey">Normalized account key.</param>
        [LoggerMessage(EventName = "SessionAuthenticated", Level = LogLevel.Information, Message = "Session authenticated SessionId={SessionId} Username={Username} AccountKey={AccountKey}")]
        public static partial void SessionAuthenticated(ILogger logger, string sessionId, string username, string accountKey);
    }
}
