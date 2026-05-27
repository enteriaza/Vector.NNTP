// <copyright file="NntpSocketHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: source-generated LoggerMessage methods for NntpSocketHostedService.

namespace Vector.NNTP.Sockets.Hosting
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="NntpSocketHostedService"/>.
    /// </summary>
    internal sealed partial class NntpSocketHostedService
    {
        /// <summary>
        /// Logs hosted-service startup before the accept loop runs.
        /// </summary>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "NNTP socket hosted service starting")]
        private partial void LogStarting();
    }
}
