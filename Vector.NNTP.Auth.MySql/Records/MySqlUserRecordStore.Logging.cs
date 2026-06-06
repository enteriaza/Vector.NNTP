// <copyright file="MySqlUserRecordStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <remarks>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlUserRecordStore"/>.
    /// </remarks>
    internal sealed partial class MySqlUserRecordStore
    {
        /// <summary>
        /// Logs the start of a user record lookup.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountName">Account name being looked up.</param>
        /// <param name="isAsync">Whether the async lookup path is in use.</param>
        [LoggerMessage(
            EventId = 400,
            Level = LogLevel.Debug,
            Message = "Auth.MySql user lookup started for '{AccountName}' (Async={IsAsync})")]
        private static partial void UserLookupStarted(ILogger logger, string accountName, bool isAsync);

        /// <summary>
        /// Logs that no user record was found.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountName">Account name being looked up.</param>
        [LoggerMessage(
            EventId = 401,
            Level = LogLevel.Debug,
            Message = "Auth.MySql user lookup not found for '{AccountName}'")]
        private static partial void UserLookupNotFound(ILogger logger, string accountName);

        /// <summary>
        /// Logs that a user record was materialised successfully.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="accountName">Account name being looked up.</param>
        [LoggerMessage(
            EventId = 402,
            Level = LogLevel.Debug,
            Message = "Auth.MySql user lookup succeeded for '{AccountName}'")]
        private static partial void UserLookupSucceeded(ILogger logger, string accountName);

        /// <summary>
        /// Logs that a user record lookup failed due to a backend error.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="ex">Underlying exception.</param>
        /// <param name="accountName">Account name being looked up.</param>
        /// <param name="failureReason">Classified failure reason.</param>
        [LoggerMessage(
            EventId = 403,
            Level = LogLevel.Error,
            Message = "Auth.MySql user lookup failed for '{AccountName}' (Reason={FailureReason})")]
        private static partial void UserLookupFailed(
            ILogger logger,
            Exception ex,
            string accountName,
            AuthMySqlFailureReason failureReason);
    }
}
