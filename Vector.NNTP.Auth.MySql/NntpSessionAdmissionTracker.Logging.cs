// <copyright file="NntpSessionAdmissionTracker.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="NntpSessionAdmissionTracker"/>.
    /// </summary>
    internal static partial class NntpSessionAdmissionTrackerLog
    {
        /// <summary>
        /// Logs that the per-account session limit prevented admission.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Account name.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="sessionLimit">Configured session limit.</param>
        [LoggerMessage(
            EventId = 400,
            Level = LogLevel.Warning,
            Message = "Session admission rejected for user '{Username}' from {ClientIp}: session limit exceeded (Limit={SessionLimit})")]
        public static partial void SessionLimitExceeded(
            ILogger logger,
            string username,
            string clientIp,
            int sessionLimit);

        /// <summary>
        /// Logs that the per-account per-source-IP session limit prevented admission.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Account name.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="srcIpLimit">Configured per-IP limit.</param>
        [LoggerMessage(
            EventId = 401,
            Level = LogLevel.Warning,
            Message = "Session admission rejected for user '{Username}' from {ClientIp}: per-IP session limit exceeded (Limit={SrcIpLimit})")]
        public static partial void SrcIpLimitExceeded(
            ILogger logger,
            string username,
            string clientIp,
            int srcIpLimit);
    }
}
