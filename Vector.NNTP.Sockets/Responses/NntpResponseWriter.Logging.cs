// <copyright file="NntpResponseWriter.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: source-generated LoggerMessage methods for NntpResponseWriter.

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Responses
{
    /// <summary>
    /// Extension methods for logging NNTP responses.
    /// </summary>
    internal static partial class NntpResponseWriterLogging
    {
        /// <summary>
        /// Logs a response status line with command-processing duration.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="connectionPrefix">Bracketed client endpoint prefix for log correlation.</param>
        /// <param name="line">Response status line.</param>
        /// <param name="elapsedMs">Elapsed milliseconds since command dispatch began.</param>
        [LoggerMessage(
            EventId = 0,
            Level = LogLevel.Debug,
            Message = "{ConnectionPrefix} TX: {Line} ({ElapsedMs:F2} ms)")]
        public static partial void LogResponseLine(this ILogger logger, string connectionPrefix, string line, double elapsedMs);

        /// <summary>
        /// Logs a response status line without duration (pre-command greeting).
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="connectionPrefix">Bracketed client endpoint prefix for log correlation.</param>
        /// <param name="line">Response status line.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "{ConnectionPrefix} TX: {Line}")]
        public static partial void LogResponseLineWithoutDuration(this ILogger logger, string connectionPrefix, string line);
    }
}
