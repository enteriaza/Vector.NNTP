// <copyright file="NntpNewsLog.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 400-419 (INN news log Serilog sink fault diagnostics).

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Source-generated logging partial for <see cref="NntpNewsLog"/> Serilog sink failures.
    /// </summary>
    internal sealed partial class NntpNewsLog
    {
        /// <summary>
        /// Logs a Serilog news sink failure without faulting the spool pipeline.
        /// </summary>
        /// <param name="logger">Host category logger.</param>
        /// <param name="exception">Observed sink exception.</param>
        [LoggerMessage(
            EventId = 400,
            Level = LogLevel.Warning,
            Message = "Failed to write INN news log line; spool pipeline continues.")]
        private static partial void LogSinkFailure(ILogger logger, Exception exception);
    }
}
