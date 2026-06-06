// <copyright file="MySqlNntpCredentialValidator.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Session.Policy;

namespace Vector.NNTP.Auth.MySql.Credentials
{
    /// <remarks>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlNntpCredentialValidator"/>.
    /// </remarks>
    internal sealed partial class MySqlNntpCredentialValidator
    {
        /// <summary>
        /// Logs that host-side authentication finalization is starting.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="isTls">Whether the connection is TLS-protected.</param>
        [LoggerMessage(
            EventId = 200,
            Level = LogLevel.Debug,
            Message = "Auth.MySql finalizing {Mechanism} authentication for user '{Username}' from {ClientIp} (TLS={IsTls})")]
        private static partial void AuthenticationFinalizing(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp,
            bool isTls);

        /// <summary>
        /// Logs that a user was not found or did not have a usable record.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="clientIp">Client IP address.</param>
        [LoggerMessage(
            EventId = 201,
            Level = LogLevel.Debug,
            Message = "Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: user not found")]
        private static partial void AuthenticationRejectedUserNotFound(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp);

        /// <summary>
        /// Logs that a user is disabled.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="clientIp">Client IP address.</param>
        [LoggerMessage(
            EventId = 202,
            Level = LogLevel.Warning,
            Message = "Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: account disabled")]
        private static partial void AuthenticationRejectedAccountDisabled(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp);

        /// <summary>
        /// Logs that credentials did not match or the mechanism is not permitted.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="clientIp">Client IP address.</param>
        [LoggerMessage(
            EventId = 203,
            Level = LogLevel.Debug,
            Message = "Auth.MySql {Mechanism} authentication rejected for user '{Username}' from {ClientIp}: invalid credentials")]
        private static partial void AuthenticationRejectedInvalidCredentials(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp);

        /// <summary>
        /// Logs that authentication succeeded and a policy was issued.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">Authenticated username.</param>
        /// <param name="clientIp">Client IP address.</param>
        /// <param name="allowPosting">Whether posting is permitted.</param>
        /// <param name="accountType">Resolved session account type from policy materialisation.</param>
        /// <param name="customerId">Customer identifier.</param>
        [LoggerMessage(
            EventId = 204,
            Level = LogLevel.Information,
            Message = "Auth.MySql {Mechanism} authentication succeeded for user '{Username}' from {ClientIp} (Posting={AllowPosting}, Type={AccountType}, CustomerId={CustomerId})")]
        private static partial void AuthenticationSucceeded(
            ILogger logger,
            string mechanism,
            string username,
            string clientIp,
            bool allowPosting,
            NntpAccountType accountType,
            string customerId);

        /// <summary>
        /// Logs that MySQL authentication failed due to an exception from the backing store.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="ex">Underlying exception for diagnostics.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="failureReason">Classified failure reason.</param>
        [LoggerMessage(
            EventId = 206,
            Level = LogLevel.Error,
            Message = "Auth.MySql {Mechanism} authentication failed for user '{Username}' due to backend error (Reason={FailureReason})")]
        private static partial void AuthenticationBackendFailed(
            ILogger logger,
            Exception ex,
            string mechanism,
            string username,
            AuthMySqlFailureReason failureReason);

        /// <summary>
        /// Logs that a transient backend failure will return 503 semantics to the caller.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="mechanism">Authentication mechanism label.</param>
        /// <param name="username">User being authenticated.</param>
        /// <param name="failureReason">Classified failure reason.</param>
        [LoggerMessage(
            EventId = 207,
            Level = LogLevel.Debug,
            Message = "Auth.MySql {Mechanism} authentication transient failure for user '{Username}' (Reason={FailureReason})")]
        private static partial void AuthenticationTransientFailure(
            ILogger logger,
            string mechanism,
            string username,
            AuthMySqlFailureReason failureReason);

        /// <summary>
        /// Logs that a SASL exchange was abandoned and the AsyncLocal cache cleared.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        [LoggerMessage(
            EventId = 208,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL exchange abandoned; per-exchange record cache cleared")]
        private static partial void SaslExchangeAbandoned(ILogger logger);

        /// <summary>
        /// Logs a per-exchange SASL cache hit during finalize.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Authenticated username.</param>
        [LoggerMessage(
            EventId = 209,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL per-exchange cache hit for user '{Username}'")]
        private static partial void SaslCacheHit(ILogger logger, string username);

        /// <summary>
        /// Logs a per-exchange SASL cache miss during finalize.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="username">Authenticated username.</param>
        [LoggerMessage(
            EventId = 210,
            Level = LogLevel.Debug,
            Message = "Auth.MySql SASL per-exchange cache miss for user '{Username}'")]
        private static partial void SaslCacheMiss(ILogger logger, string username);
    }
}
