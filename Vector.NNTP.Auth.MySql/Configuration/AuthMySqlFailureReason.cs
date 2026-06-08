// <copyright file="AuthMySqlFailureReason.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: classified backend failure reasons for structured authentication logs.

namespace Vector.NNTP.Auth.MySql.Configuration
{
    /// <summary>
    /// Stable classification for unexpected MySQL authentication backend faults.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Normalises exceptions from user-record and credential-store I/O into a small vocabulary for structured
    /// logging, OpenTelemetry span status, and operator dashboards. Values are assigned by
    /// <see cref="AuthMySqlFailureClassifier.Classify"/>; callers pass the result to source-generated failure log helpers
    /// (for example <c>MySqlUserRecordStore.Logging.cs</c> and credential-store logging partials).
    /// </para>
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Records.MySqlUserRecordStore"/> — logs and sets <c>Activity.SetStatus</c> with <c>reason.ToString()</c> on
    /// lookup exceptions.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Credentials.MySqlNntpCredentialValidator"/>, <see cref="Credentials.MySqlCramMd5CredentialStore"/>, and
    /// <see cref="Credentials.MySqlScramCredentialStore"/> — failure logs before wrapping transient store exceptions.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Scope:</b> Describes infrastructure and provider failures only. Expected authentication negatives (unknown user,
    /// wrong password, disabled account, mechanism not permitted) do not produce these values.
    /// </para>
    /// <para>
    /// <b>Numeric values:</b> Explicit underlying integers (<c>0</c>–<c>4</c>) are stable for log parsing; do not reorder
    /// without updating consumers that rely on enum names in message templates (<c>Reason={FailureReason}</c>).
    /// </para>
    /// <para>
    /// <b>Cancellation note:</b> <see cref="Cancelled"/> is returned when <see cref="AuthMySqlFailureClassifier"/> sees
    /// <see cref="OperationCanceledException"/>. SASL credential stores rethrow cancellation before classification; the
    /// <see cref="Cancelled"/> label appears primarily on <see cref="Records.MySqlUserRecordStore"/> async lookup paths.
    /// </para>
    /// </remarks>
    internal enum AuthMySqlFailureReason
    {
        /// <summary>
        /// The exception could not be mapped to a more specific reason.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returned when <see cref="AuthMySqlFailureClassifier.Classify"/> exhausts type checks and
        /// <see cref="MySqlConnector.MySqlException"/> heuristics without a match, including unrecognised
        /// <see cref="MySqlConnector.MySqlErrorCode"/> values whose messages lack timeout or pool keywords.
        /// </para>
        /// <para>Underlying value <c>0</c>.</para>
        /// </remarks>
        Unknown = 0,

        /// <summary>
        /// The client could not establish a usable connection to the database server in time.
        /// </summary>
        /// <remarks>
        /// <para><b>Classifier sources:</b></para>
        /// <list type="bullet">
        /// <item><description>Outer <see cref="IOException"/> (network/TLS connect faults).</description></item>
        /// <item>
        /// <description>
        /// <see cref="MySqlConnector.MySqlException"/> with <see cref="MySqlConnector.MySqlErrorCode.UnableToConnectToHost"/>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// MySQL exception messages containing <c>timeout</c> or <c>timed out</c> together with <c>connect</c>
        /// (case-insensitive).
        /// </description>
        /// </item>
        /// </list>
        /// <para>Underlying value <c>1</c>.</para>
        /// </remarks>
        ConnectTimeout = 1,

        /// <summary>
        /// A command or read operation exceeded its time limit on an established connection.
        /// </summary>
        /// <remarks>
        /// <para><b>Classifier sources:</b></para>
        /// <list type="bullet">
        /// <item><description>Outer <see cref="TimeoutException"/>.</description></item>
        /// <item>
        /// <description>
        /// MySQL exception messages containing <c>timeout</c> or <c>timed out</c> without <c>connect</c> in the message
        /// (case-insensitive substring heuristics).
        /// </description>
        /// </item>
        /// </list>
        /// <para>Typical when <c>DefaultCommandTimeout</c> or per-command timeout is exceeded during
        /// <c>nntpusers</c> lookup SQL.</para>
        /// <para>Underlying value <c>2</c>.</para>
        /// </remarks>
        QueryTimeout = 2,

        /// <summary>
        /// The connection pool could not supply a connection within the allowed wait.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returned when a <see cref="MySqlConnector.MySqlException"/> message contains <c>pool</c> (case-insensitive).
        /// Indicates contention or exhaustion under burst NNRPD authentication load rather than a single slow query.
        /// </para>
        /// <para>Underlying value <c>3</c>.</para>
        /// </remarks>
        PoolPressure = 3,

        /// <summary>
        /// The database operation was aborted by cancellation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returned when <see cref="AuthMySqlFailureClassifier.Classify"/> receives
        /// <see cref="OperationCanceledException"/> (including cooperative shutdown or an async lookup
        /// <see cref="CancellationToken"/>). Not logged by credential-store helpers that rethrow cancellation before
        /// classification.
        /// </para>
        /// <para>Underlying value <c>4</c>.</para>
        /// </remarks>
        Cancelled = 4,
    }
}
