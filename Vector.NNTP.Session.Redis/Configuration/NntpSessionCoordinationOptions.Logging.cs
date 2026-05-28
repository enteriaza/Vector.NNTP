// <copyright file="NntpSessionCoordinationOptions.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Configuration
{
    /// <summary>
    /// Source-generated logging for <see cref="NntpSessionCoordinationOptions"/> validation.
    /// </summary>
    public sealed partial class NntpSessionCoordinationOptions
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Redis:Hosts contains duplicate entry '{Host}'.")]
        private static partial void LogDuplicateHostWarning(ILogger logger, string host);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Redis coordination options validated: HostCount={HostCount} Port={Port} Retry={Retry} TimeoutSeconds={TimeoutSeconds} MinConnections={MinConnections} MaxConnections={MaxConnections}.")]
        private static partial void LogValidationSuccess(
            ILogger logger,
            int hostCount,
            int port,
            int retry,
            int timeoutSeconds,
            int minConnections,
            int maxConnections);
    }
}
