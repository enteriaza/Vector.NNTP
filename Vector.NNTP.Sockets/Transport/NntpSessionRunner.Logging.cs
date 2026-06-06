// <copyright file="NntpSessionRunner.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Logging for <see cref="NntpSessionRunner"/>.
    /// </summary>
    internal static partial class NntpSessionRunnerLog
    {
        /// <summary>
        /// Logs that a session has been teared down.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="reason">Reason for teardown.</param>
        [LoggerMessage(EventName = "SessionRemoved", Level = LogLevel.Debug, Message = "Session teardown SessionId={SessionId} Reason={Reason}")]
        public static partial void SessionTeardown(ILogger logger, string sessionId, string reason);

        /// <summary>
        /// Logs that a session admission release failed.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="exception">Exception that occurred.</param>
        /// <param name="sessionId">Session identifier.</param>
        [LoggerMessage(Level = LogLevel.Warning, Message = "Admission release failed SessionId={SessionId}")]
        public static partial void AdmissionReleaseFailed(ILogger logger, Exception exception, string sessionId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Transit peer metrics decrement failed SessionId={SessionId}")]
        public static partial void TransitPeerMetricsFailed(ILogger logger, Exception exception, string sessionId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Transit peer Redis release failed SessionId={SessionId} PeerId={PeerId}")]
        public static partial void TransitPeerReleaseFailed(
            ILogger logger,
            Exception exception,
            string sessionId,
            string peerId);

        /// <summary>
        /// Logs an expected client disconnect during the command loop (for example reset after rejecting offers).
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="exception">Disconnect exception.</param>
        /// <param name="sessionId">Session identifier.</param>
        [LoggerMessage(Level = LogLevel.Debug, Message = "Client disconnect SessionId={SessionId}")]
        public static partial void SessionClientDisconnect(ILogger logger, Exception exception, string sessionId);

        /// <summary>
        /// Logs an unhandled session-loop failure before emitting <c>503 Program fault</c>.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="exception">Unhandled exception.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="transitPeerId">Transit peer identifier when the connection is a trusted peer; otherwise empty.</param>
        [LoggerMessage(Level = LogLevel.Error, Message = "Session program fault SessionId={SessionId} TransitPeerId={TransitPeerId}")]
        public static partial void SessionProgramFault(
            ILogger logger,
            Exception exception,
            string sessionId,
            string transitPeerId);
    }
}
