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
        [LoggerMessage(EventName = "SessionRemoved", Level = LogLevel.Debug, Message = "Session teardown SessionId={SessionId} Reason={Reason}")]
        public static partial void SessionTeardown(ILogger logger, string sessionId, string reason);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Admission release failed SessionId={SessionId}")]
        public static partial void AdmissionReleaseFailed(ILogger logger, Exception exception, string sessionId);
    }
}
