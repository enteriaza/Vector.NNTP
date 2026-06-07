// <copyright file="MySqlAuthConnectivityValidator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Auth.MySql.HostedServices
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlAuthConnectivityValidator"/>.
    /// </summary>
    /// <remarks>
    /// Cold-path logging for fail-fast <c>SELECT 1</c> connectivity validation at host startup.
    /// </remarks>
    internal sealed partial class MySqlAuthConnectivityValidator
    {
        /// <summary>
        /// Logs successful startup connectivity validation.
        /// </summary>
        /// <param name="logger">Logger for startup connectivity validation.</param>
        [LoggerMessage(
            EventId = 110,
            Level = LogLevel.Information,
            Message = "Auth.MySql startup connectivity validation succeeded (SELECT 1)")]
        private static partial void ConnectivityValidationSucceeded(ILogger logger);

        /// <summary>
        /// Logs failed startup connectivity validation.
        /// </summary>
        /// <param name="logger">Logger for startup connectivity validation.</param>
        /// <param name="ex">Underlying exception.</param>
        [LoggerMessage(
            EventId = 111,
            Level = LogLevel.Error,
            Message = "Auth.MySql startup connectivity validation failed")]
        private static partial void ConnectivityValidationFailed(ILogger logger, Exception ex);
    }
}
