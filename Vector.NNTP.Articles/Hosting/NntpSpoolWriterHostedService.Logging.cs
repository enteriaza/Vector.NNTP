// <copyright file="NntpSpoolWriterHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 200-219 (spool writer hosted service lifecycle and scaling loop faults).

namespace Vector.NNTP.Articles.Hosting
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> logging partial for
    /// <see cref="NntpSpoolWriterHostedService"/> lifecycle and scaling-loop diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>EventId band:</b> 200-219 on <c>ILogger&lt;NntpSpoolWriterHostedService&gt;</c>. Pool scaling transitions
    /// use EventIds 200-201 on <c>ILogger&lt;NntpSpoolWriterPool&gt;</c>.
    /// </para>
    /// </remarks>
    internal sealed partial class NntpSpoolWriterHostedService
    {
        /// <summary>
        /// Logs that the spool writer hosted service started its scaling loop.
        /// </summary>
        /// <param name="logger">Hosted service category logger.</param>
        [LoggerMessage(
            EventId = 200,
            Level = LogLevel.Information,
            Message = "Spool writer hosted service started.")]
        private static partial void LogServiceStarted(ILogger logger);

        /// <summary>
        /// Logs that the spool writer hosted service scaling loop stopped.
        /// </summary>
        /// <param name="logger">Hosted service category logger.</param>
        [LoggerMessage(
            EventId = 201,
            Level = LogLevel.Information,
            Message = "Spool writer hosted service stopped.")]
        private static partial void LogServiceStopped(ILogger logger);

        /// <summary>
        /// Logs a scaling-loop failure without crashing the host.
        /// </summary>
        /// <param name="logger">Hosted service category logger.</param>
        /// <param name="exception">Observed exception from pool scaling.</param>
        [LoggerMessage(
            EventId = 202,
            Level = LogLevel.Error,
            Message = "Spool writer scaling loop failed; continuing until next tick.")]
        private static partial void LogScalingLoopFailure(ILogger logger, Exception exception);
    }
}
