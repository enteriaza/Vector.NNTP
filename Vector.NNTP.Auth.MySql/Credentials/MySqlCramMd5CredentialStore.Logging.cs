// <copyright file="MySqlCramMd5CredentialStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <remarks>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlCramMd5CredentialStore"/>.
    /// </remarks>
    public sealed partial class MySqlCramMd5CredentialStore
    {
        /// <summary>
        /// Logs the start of a CRAM-MD5 secret lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 300,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup started for user '{Username}'")]
        private static partial void CramLookupStarted(ILogger logger, string username);

        /// <summary>
        /// Logs that no record was found for a CRAM-MD5 secret lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 301,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': user not found")]
        private static partial void CramLookupUserNotFound(ILogger logger, string username);

        /// <summary>
        /// Logs that the account is disabled during a CRAM-MD5 secret lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 302,
            Level = LogLevel.Warning,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': account disabled")]
        private static partial void CramLookupAccountDisabled(ILogger logger, string username);

        /// <summary>
        /// Logs that CRAM-MD5 is not permitted for the account.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 305,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': password-based authentication not permitted")]
        private static partial void CramLookupNotPermitted(ILogger logger, string username);

        /// <summary>
        /// Logs that the stored password is not US-ASCII and cannot be used as a CRAM-MD5 secret.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 306,
            Level = LogLevel.Warning,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup rejected for user '{Username}': non-ASCII password")]
        private static partial void CramLookupNonAsciiPassword(ILogger logger, string username);

        /// <summary>
        /// Logs that a CRAM-MD5 secret lookup succeeded.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 303,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup succeeded for user '{Username}'")]
        private static partial void CramLookupSucceeded(ILogger logger, string username);

        /// <summary>
        /// Logs that a CRAM-MD5 secret lookup failed due to an exception.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="ex">Underlying exception.</param>
        /// <param name="username">Username being looked up.</param>
        /// <param name="failureReason">Classified failure reason.</param>
        [LoggerMessage(
            EventId = 304,
            Level = LogLevel.Error,
            Message = "Auth.MySql SASL CRAM-MD5 credential lookup failed for user '{Username}' due to backend error (Reason={FailureReason})")]
        private static partial void CramLookupFailed(
            ILogger logger,
            Exception ex,
            string username,
            AuthMySqlFailureReason failureReason);
    }
}
