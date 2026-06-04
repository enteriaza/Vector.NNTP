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
        /// Logs a response line being sent to the client.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="line">Response line.</param>
        [LoggerMessage(
            EventId = 0,
            Level = LogLevel.Debug,
            Message = "TX: {Line}")]
        public static partial void LogResponseLine(this ILogger logger, string line);
    }
}
