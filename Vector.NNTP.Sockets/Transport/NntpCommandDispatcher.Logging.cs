// <copyright file="NntpCommandDispatcher.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: source-generated LoggerMessage methods for NntpCommandDispatcher.

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="NntpCommandDispatcher"/>.
    /// </summary>
    /// <remarks>
    /// Cold-path logging invoked from the per-session command loop. Methods are <see langword="static"/> with an explicit
    /// <see cref="ILogger"/> parameter so source generation stays valid and call sites remain analyzable.
    /// </remarks>
    internal static partial class NntpCommandDispatcherLog
    {
        /// <summary>
        /// Logs a command received from the client.
        /// </summary>
        /// <param name="logger">Logger for the dispatcher instance.</param>
        /// <param name="connectionPrefix">Bracketed client endpoint prefix for log correlation.</param>
        /// <param name="command">Redacted command line.</param>
        /// <remarks>
        /// Suppressed while a multi-line body is pending or DEFLATE compression is active so binary payloads are not logged.
        /// </remarks>
        [LoggerMessage(
            EventId = 0,
            Level = LogLevel.Debug,
            Message = "{ConnectionPrefix} RX: {Command}")]
        public static partial void LogCommandReceived(ILogger logger, string connectionPrefix, string command);

        /// <summary>
        /// Logs an unrecognized client command after redaction of sensitive substrings.
        /// </summary>
        /// <param name="logger">Logger for the dispatcher instance.</param>
        /// <param name="line">Redacted command line.</param>
        /// <remarks>
        /// Emitted at debug level when verb classification returns <see cref="NntpKnownVerb.Unknown"/> or an unhandled enum value.
        /// </remarks>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "Unknown command: {Line}")]
        public static partial void LogUnknownCommand(ILogger logger, string line);

        /// <summary>
        /// Logs CPU overload rejection on an established session (RFC 3977 §3.2.1).
        /// </summary>
        /// <param name="logger">Logger for the dispatcher instance.</param>
        /// <param name="connectionPrefix">Connection log prefix.</param>
        /// <param name="effectiveCpuUtilizationPercent">Effective EWMA percent driving the gate.</param>
        /// <param name="dominantSignal">Signal with the highest EWMA.</param>
        /// <param name="processEwmaPercent">Process EWMA percent when enabled.</param>
        /// <param name="hostEwmaPercent">Host EWMA percent when enabled.</param>
        /// <param name="cgroupEwmaPercent">Cgroup EWMA percent when available.</param>
        /// <param name="gateState">Gate state label.</param>
        /// <param name="rejectThresholdPercent">Reject threshold.</param>
        /// <param name="resumeThresholdPercent">Resume threshold.</param>
        /// <remarks>
        /// Called at the start of <see cref="NntpCommandDispatcher.DispatchBytesAsync"/> when
        /// <see cref="NntpServerOptions.CpuRejectEnabled"/> is true and <see cref="INntpCpuLoadMonitor.IsOverloaded"/>
        /// returns true, immediately before the <c>400</c> response.
        /// </remarks>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "{ConnectionPrefix} Rejecting connection due to CPU overload. EffectiveCpuUtilizationPercent={EffectiveCpuUtilizationPercent} DominantSignal={DominantSignal} ProcessEwmaPercent={ProcessEwmaPercent} HostEwmaPercent={HostEwmaPercent} CgroupEwmaPercent={CgroupEwmaPercent} GateState={GateState} RejectThresholdPercent={RejectThresholdPercent} ResumeThresholdPercent={ResumeThresholdPercent}")]
        public static partial void LogCpuOverloadRejectCommand(
            ILogger logger,
            string connectionPrefix,
            double effectiveCpuUtilizationPercent,
            string dominantSignal,
            double? processEwmaPercent,
            double? hostEwmaPercent,
            double? cgroupEwmaPercent,
            string gateState,
            double rejectThresholdPercent,
            double resumeThresholdPercent);
    }
}
