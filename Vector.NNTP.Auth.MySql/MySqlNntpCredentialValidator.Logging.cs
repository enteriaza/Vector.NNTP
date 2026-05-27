// <copyright file="MySqlNntpCredentialValidator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlNntpCredentialValidator"/>.
    /// </summary>
    internal static partial class MySqlNntpCredentialValidatorLog
    {
        /// <summary>
        /// Logs that MySQL credential validation failed due to an exception from the backing store.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="ex">Underlying exception for diagnostics.</param>
        /// <param name="username">User being authenticated.</param>
        [LoggerMessage(
            EventId = 200,
            Level = LogLevel.Warning,
            Message = "MySQL credential validation failed for user '{Username}'")]
        public static partial void CredentialValidationBackendFailed(ILogger logger, Exception ex, string username);
    }
}
