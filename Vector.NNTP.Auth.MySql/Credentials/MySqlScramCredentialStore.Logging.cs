// <copyright file="MySqlScramCredentialStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <remarks>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlScramCredentialStore"/>.
    /// </remarks>
    internal sealed partial class MySqlScramCredentialStore
    {
        /// <summary>
        /// Logs the start of a SCRAM credential lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 320,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup started for user '{Username}'")]
        private static partial void ScramLookupStarted(ILogger logger, string username);

        /// <summary>
        /// Logs that no record was found for a SCRAM credential lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 321,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': user not found")]
        private static partial void ScramLookupUserNotFound(ILogger logger, string username);

        /// <summary>
        /// Logs that the account is disabled during a SCRAM credential lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 322,
            Level = LogLevel.Warning,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': account disabled")]
        private static partial void ScramLookupAccountDisabled(ILogger logger, string username);

        /// <summary>
        /// Logs that SCRAM is not permitted for the account.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 323,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': SCRAM-SHA-256 not permitted")]
        private static partial void ScramLookupNotPermitted(ILogger logger, string username);

        /// <summary>
        /// Logs that SCRAM stored-key material is missing.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        [LoggerMessage(
            EventId = 324,
            Level = LogLevel.Warning,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup rejected for user '{Username}': SCRAM material missing")]
        private static partial void ScramLookupMaterialMissing(ILogger logger, string username);

        /// <summary>
        /// Logs that a SCRAM credential lookup succeeded.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Username being looked up.</param>
        /// <param name="iterations">Configured SCRAM iteration count.</param>
        [LoggerMessage(
            EventId = 325,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup succeeded for user '{Username}' (Iterations={Iterations})")]
        private static partial void ScramLookupSucceeded(ILogger logger, string username, int iterations);

        /// <summary>
        /// Logs that a SCRAM credential lookup failed due to an exception.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="ex">Underlying exception.</param>
        /// <param name="username">Username being looked up.</param>
        /// <param name="failureReason">Classified failure reason.</param>
        [LoggerMessage(
            EventId = 326,
            Level = LogLevel.Error,
            Message = "Auth.MySql SASL SCRAM-SHA-256 credential lookup failed for user '{Username}' due to backend error (Reason={FailureReason})")]
        private static partial void ScramLookupFailed(
            ILogger logger,
            Exception ex,
            string username,
            AuthMySqlFailureReason failureReason);
    }
}
