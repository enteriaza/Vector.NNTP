// <copyright file="MySqlUserRecordStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 400-403 (MySQL user-record lookup lifecycle).

using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="MySqlUserRecordStore"/> lookup
    /// lifecycle logging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Logging partial for <see cref="MySqlUserRecordStore"/>. Emits structured lookup lifecycle events from
    /// <c>ExecuteLookup</c> and <c>ExecuteLookupAsync</c> around parameterised <c>nntpusers</c> queries. Authentication
    /// cache hits are logged by <see cref="CachingMySqlUserRecordStore"/> (EventIds <c>420</c>–<c>421</c>), not here.
    /// </para>
    /// <para>
    /// <b>Logger category:</b> Callers pass <see cref="ILogger{MySqlUserRecordStore}"/> from the store instance. Methods are
    /// <see langword="static"/> <see langword="partial"/> with an explicit <see cref="ILogger"/> parameter so
    /// <see cref="LoggerMessageAttribute"/> source generation remains valid on the nested partial class.
    /// </para>
    /// <para><b>Event identifiers:</b></para>
    /// <list type="bullet">
    /// <item><description>EventId <c>400</c> — lookup started (<see cref="UserLookupStarted"/>).</description></item>
    /// <item><description>EventId <c>401</c> — row not found (<see cref="UserLookupNotFound"/>).</description></item>
    /// <item><description>EventId <c>402</c> — row materialised (<see cref="UserLookupSucceeded"/>).</description></item>
    /// <item><description>EventId <c>403</c> — backend fault (<see cref="UserLookupFailed"/>).</description></item>
    /// </list>
    /// <para>
    /// <b>Observability pairing:</b> Each lookup also records <see cref="Telemetry.AuthMySqlMetrics"/> outcomes and duration
    /// and may emit an <see cref="Telemetry.AuthMySqlTelemetry"/> <c>auth.mysql.user.lookup</c> span. Metrics and traces do
    /// not replace these log lines for operator grep workflows.
    /// </para>
    /// <para>
    /// <b>Privacy:</b> Logs include the plaintext account name for correlation; decrypted passwords,
    /// SCRAM keys, and SQL bind details are never written by these helpers.
    /// </para>
    /// <para><b>Threading:</b> Static helpers; safe to call from concurrent session handlers on the singleton store.</para>
    /// </remarks>
    internal sealed partial class MySqlUserRecordStore
    {
        /// <summary>
        /// Logs the start of a MySQL user-record lookup before connection open.
        /// </summary>
        /// <param name="logger">
        /// Store category logger (typically <see cref="ILogger{MySqlUserRecordStore}"/>). Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="accountName">
        /// NNTP account name bound to <c>@account_name</c> in the lookup query. Rendered as <c>'{AccountName}'</c> in the
        /// message template.
        /// </param>
        /// <param name="isAsync">
        /// <see langword="true"/> when invoked from <c>ExecuteLookupAsync</c>; <see langword="false"/> for the synchronous
        /// <c>ExecuteLookup</c> path. Rendered as <c>Async=</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>400</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql user lookup started for '{AccountName}' (Async={IsAsync})</c>.
        /// </para>
        /// <para>
        /// Emitted once per database lookup attempt before <see cref="MySqlConnector.MySqlConnection.Open"/> or
        /// <c>OpenAsync</c>. Not emitted for authentication cache hits that bypass this store.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 400,
            Level = LogLevel.Debug,
            Message = "Auth.MySql user lookup started for '{AccountName}' (Async={IsAsync})")]
        private static partial void UserLookupStarted(ILogger logger, string accountName, bool isAsync);

        /// <summary>
        /// Logs that the lookup query completed successfully but returned no matching row.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="accountName">
        /// Account name that was not found in <c>nntpusers</c>. Rendered as <c>'{AccountName}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>401</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql user lookup not found for '{AccountName}'</c>.
        /// </para>
        /// <para>
        /// Invoked when <see cref="MySqlConnector.MySqlDataReader.Read"/> (or <c>ReadAsync</c>) returns
        /// <see langword="false"/>. Pair with <see cref="Telemetry.AuthMySqlMetrics.RecordLookup"/> using outcome
        /// <c>not_found</c>. This is an expected authentication outcome, not an error-level fault.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 401,
            Level = LogLevel.Debug,
            Message = "Auth.MySql user lookup not found for '{AccountName}'")]
        private static partial void UserLookupNotFound(ILogger logger, string accountName);

        /// <summary>
        /// Logs that a user record row was read and mapped successfully.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="accountName">
        /// Account name for the row that was materialised. Rendered as <c>'{AccountName}'</c> in the message template.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>402</c>, <see cref="LogLevel.Debug"/>. Message template:
        /// <c>Auth.MySql user lookup succeeded for '{AccountName}'</c>.
        /// </para>
        /// <para>
        /// Emitted after <see cref="MapUserRecord"/> succeeds. Pair with
        /// <see cref="Telemetry.AuthMySqlMetrics.RecordLookup"/> using outcome <c>found</c>. Does not log decrypted field
        /// values from the row.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 402,
            Level = LogLevel.Debug,
            Message = "Auth.MySql user lookup succeeded for '{AccountName}'")]
        private static partial void UserLookupSucceeded(ILogger logger, string accountName);

        /// <summary>
        /// Logs a backend fault during user-record lookup and attaches the exception to the log entry.
        /// </summary>
        /// <param name="logger">Store category logger. Must not be <see langword="null"/>.</param>
        /// <param name="ex">
        /// Exception raised during connection, command execution, or reader mapping. Recorded on the log event by the
        /// source-generated helper even though the message template lists only <c>Reason</c> and <c>AccountName</c>.
        /// </param>
        /// <param name="accountName">
        /// Account name for the lookup in flight. Rendered as <c>'{AccountName}'</c> in the message template.
        /// </param>
        /// <param name="failureReason">
        /// Stable reason from <see cref="AuthMySqlFailureClassifier.Classify"/>, rendered as <c>Reason=</c> in the message
        /// template (for example <see cref="AuthMySqlFailureReason.ConnectTimeout"/> or
        /// <see cref="AuthMySqlFailureReason.Cancelled"/>).
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated at EventId <c>403</c>, <see cref="LogLevel.Error"/>. Message template:
        /// <c>Auth.MySql user lookup failed for '{AccountName}' (Reason={FailureReason})</c>.
        /// </para>
        /// <para>
        /// Invoked from the <c>catch</c> block in <c>ExecuteLookup</c> / <c>ExecuteLookupAsync</c> before the exception is
        /// rethrown. Pair with <see cref="Telemetry.AuthMySqlMetrics.RecordLookup"/> outcome <c>transient_failure</c> and
        /// OpenTelemetry error status on the <c>auth.mysql.user.lookup</c> span when tracing is enabled.
        /// </para>
        /// <para>Never swallows <paramref name="ex"/>; callers always rethrow after logging.</para>
        /// </remarks>
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
