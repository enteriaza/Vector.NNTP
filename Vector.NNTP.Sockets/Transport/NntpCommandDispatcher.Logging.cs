// <copyright file="NntpCommandDispatcher.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: source-generated LoggerMessage methods for NntpCommandDispatcher.

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="NntpCommandDispatcher"/>.
    /// </summary>
    public sealed partial class NntpCommandDispatcher
    {
        /// <summary>
        /// Logs a command received from the client.
        /// </summary>
        /// <param name="connectionPrefix">Bracketed client endpoint prefix for log correlation.</param>
        /// <param name="command">Redacted command line.</param>
        [LoggerMessage(
            EventId = 0,
            Level = LogLevel.Debug,
            Message = "{ConnectionPrefix} RX: {Command}")]
        private partial void LogCommandReceived(string connectionPrefix, string command);

        /// <summary>
        /// Logs an unrecognized client command after redaction of sensitive substrings.
        /// </summary>
        /// <param name="line">Redacted command line.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "Unknown command: {Line}")]
        private partial void LogUnknownCommand(string line);
    }
}
