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
        /// Logs the start of a credential validation attempt.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        [LoggerMessage(
            EventId = 200,
            Level = LogLevel.Debug,
            Message = "Validating NNTP credentials via MySQL for user '{Username}' from {ClientIp} (TLS={IsTls})")]
        public static partial void ValidationAttemptStarted(ILogger logger, string username, string clientIp, bool isTls);

        /// <summary>
        /// Logs that a user was not found or did not have a usable record.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="clientIp">Client IP address.</param>
        [LoggerMessage(
            EventId = 201,
            Level = LogLevel.Debug,
            Message = "MySQL credential validation rejected for user '{Username}' from {ClientIp}: user not found")]
        public static partial void ValidationRejectedUserNotFound(ILogger logger, string username, string clientIp);

        /// <summary>
        /// Logs that a user is disabled.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="clientIp">Client IP address.</param>
        [LoggerMessage(
            EventId = 202,
            Level = LogLevel.Warning,
            Message = "MySQL credential validation rejected for user '{Username}' from {ClientIp}: account disabled")]
        public static partial void ValidationRejectedAccountDisabled(ILogger logger, string username, string clientIp);

        /// <summary>
        /// Logs that a password did not match.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="clientIp">Client IP address.</param>
        [LoggerMessage(
            EventId = 203,
            Level = LogLevel.Debug,
            Message = "MySQL credential validation rejected for user '{Username}' from {ClientIp}: invalid credentials")]
        public static partial void ValidationRejectedInvalidCredentials(ILogger logger, string username, string clientIp);

        /// <summary>
        /// Logs that authentication succeeded and a policy was issued.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Authenticated username.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="allowPosting">Whether posting is permitted.</param>
        /// <param name="accountType">Account type.</param>
        /// <param name="customerId">Customer identifier.</param>
        [LoggerMessage(
            EventId = 204,
            Level = LogLevel.Information,
            Message = "MySQL authentication succeeded for user '{Username}' from {ClientIp} (Posting={AllowPosting}, Type={AccountType}, CustomerId={CustomerId})")]
        public static partial void AuthenticationSucceeded(
            ILogger logger,
            string username,
            string clientIp,
            bool allowPosting,
            char accountType,
            string customerId);

        /// <summary>
        /// Logs that authentication succeeded but admission limits blocked the session.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Authenticated username.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="sessionLimit">Configured per-account session limit.</param>
        /// <param name="srcIpLimit">Configured per-account per-IP session limit.</param>
        [LoggerMessage(
            EventId = 205,
            Level = LogLevel.Warning,
            Message = "MySQL authentication admitted user '{Username}' from {ClientIp} but admission limits blocked entry (SessionLimit={SessionLimit}, SrcIpLimit={SrcIpLimit})")]
        public static partial void AdmissionRejected(
            ILogger logger,
            string username,
            string clientIp,
            int sessionLimit,
            int srcIpLimit);

        /// <summary>
        /// Logs that MySQL credential validation failed due to an exception from the backing store.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="ex">Underlying exception for diagnostics.</param>
        /// <param name="username">User being authenticated.</param>
        [LoggerMessage(
            EventId = 206,
            Level = LogLevel.Error,
            Message = "MySQL credential validation failed for user '{Username}' due to backend error")]
        public static partial void CredentialValidationBackendFailed(ILogger logger, Exception ex, string username);
    }
}
