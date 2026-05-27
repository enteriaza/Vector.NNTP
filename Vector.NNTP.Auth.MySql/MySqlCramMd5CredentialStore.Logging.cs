// <copyright file="MySqlCramMd5CredentialStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlCramMd5CredentialStore"/>.
    /// </summary>
    internal static partial class MySqlCramMd5CredentialStoreLog
    {
        /// <summary>
        /// Logs the start of a CRAM-MD5 secret lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 300,
            Level = LogLevel.Debug,
            Message = "MySQL SASL CRAM-MD5 credential lookup started for user '{Username}'")]
        public static partial void CramLookupStarted(ILogger logger, string username);

        /// <summary>
        /// Logs that no record was found for a CRAM-MD5 secret lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 301,
            Level = LogLevel.Debug,
            Message = "MySQL SASL CRAM-MD5 credential lookup rejected for user '{Username}': user not found")]
        public static partial void CramLookupUserNotFound(ILogger logger, string username);

        /// <summary>
        /// Logs that the account is disabled during a CRAM-MD5 secret lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 302,
            Level = LogLevel.Warning,
            Message = "MySQL SASL CRAM-MD5 credential lookup rejected for user '{Username}': account disabled")]
        public static partial void CramLookupAccountDisabled(ILogger logger, string username);

        /// <summary>
        /// Logs that CRAM-MD5 is not permitted for the account.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 305,
            Level = LogLevel.Debug,
            Message = "MySQL SASL CRAM-MD5 credential lookup rejected for user '{Username}': password-based authentication not permitted")]
        public static partial void CramLookupNotPermitted(ILogger logger, string username);

        /// <summary>
        /// Logs that a CRAM-MD5 secret lookup succeeded.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 303,
            Level = LogLevel.Debug,
            Message = "MySQL SASL CRAM-MD5 credential lookup succeeded for user '{Username}'")]
        public static partial void CramLookupSucceeded(ILogger logger, string username);

        /// <summary>
        /// Logs that a CRAM-MD5 secret lookup failed due to an exception.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="ex">Underlying exception.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 304,
            Level = LogLevel.Error,
            Message = "MySQL SASL CRAM-MD5 credential lookup failed for user '{Username}' due to backend error")]
        public static partial void CramLookupFailed(ILogger logger, Exception ex, string username);
    }
}
