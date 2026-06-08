// <copyright file="AuthMySqlFailureClassifier.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: maps database exceptions to structured failure reasons for logs and metrics.

using MySqlConnector;

namespace Vector.NNTP.Auth.MySql.Configuration
{
    /// <summary>
    /// Maps MySQL authentication I/O exceptions to stable <see cref="AuthMySqlFailureReason"/> values for observability.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Shared classifier invoked on backend faults before structured failure logging in
    /// <see cref="Records.MySqlUserRecordStore"/>, <see cref="Credentials.MySqlNntpCredentialValidator"/>,
    /// <see cref="Credentials.MySqlCramMd5CredentialStore"/>, and <see cref="Credentials.MySqlScramCredentialStore"/>.
    /// The returned enum is rendered as <c>Reason={FailureReason}</c> in log templates and as span status text on lookup
    /// traces.
    /// </para>
    /// <para>
    /// <b>Scope:</b> Classifies only exceptions passed in by callers. Credential stores rethrow
    /// <see cref="OperationCanceledException"/> before calling <see cref="Classify"/>; cancellation labels therefore appear
    /// mainly from record-store lookup paths (see <see cref="AuthMySqlFailureReason.Cancelled"/>).
    /// </para>
    /// <para>
    /// <b>Algorithm:</b> <see cref="Classify"/> applies type-based rules on the outer exception, then recurses into
    /// <see cref="Exception.InnerException"/> when the outer type is not recognised. Provider-specific heuristics for
    /// <see cref="MySqlException"/> live in <see cref="ClassifyMySqlException"/>.
    /// </para>
    /// <para><b>Thread safety:</b> Stateless static helpers; safe under concurrent NNTP session handlers.</para>
    /// </remarks>
    internal static class AuthMySqlFailureClassifier
    {
        /// <summary>
        /// Classifies an authentication backend exception for logging and tracing.
        /// </summary>
        /// <param name="exception">
        /// Exception raised during <c>nntpusers</c> lookup or credential-store I/O. Must not be <see langword="null"/>.
        /// </param>
        /// <returns>
        /// A stable <see cref="AuthMySqlFailureReason"/> describing the fault. Never throws except for a
        /// <see langword="null"/> argument.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="exception"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para><b>Decision order (outer exception):</b></para>
        /// <list type="number">
        /// <item><description><see cref="OperationCanceledException"/> → <see cref="AuthMySqlFailureReason.Cancelled"/>.</description></item>
        /// <item><description><see cref="TimeoutException"/> → <see cref="AuthMySqlFailureReason.QueryTimeout"/>.</description></item>
        /// <item><description><see cref="IOException"/> → <see cref="AuthMySqlFailureReason.ConnectTimeout"/>.</description></item>
        /// <item><description><see cref="MySqlException"/> → <see cref="ClassifyMySqlException"/>.</description></item>
        /// <item>
        /// <description>
        /// Non-null <see cref="Exception.InnerException"/> → recursive <see cref="Classify"/> on the inner exception.
        /// </description>
        /// </item>
        /// <item><description>Otherwise → <see cref="AuthMySqlFailureReason.Unknown"/>.</description></item>
        /// </list>
        /// <para>
        /// Type checks on the outer exception take precedence over inner walks. A wrapped <see cref="MySqlException"/> is
        /// classified from the outer instance unless the outer type is unrecognised and an inner exception is present.
        /// </para>
        /// </remarks>
        internal static AuthMySqlFailureReason Classify(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            return exception is OperationCanceledException
                ? AuthMySqlFailureReason.Cancelled
                : exception is TimeoutException
                ? AuthMySqlFailureReason.QueryTimeout
                : exception is IOException
                ? AuthMySqlFailureReason.ConnectTimeout
                : exception is MySqlException mysql
                ? ClassifyMySqlException(mysql)
                : exception.InnerException is not null
                ? Classify(exception.InnerException)
                : AuthMySqlFailureReason.Unknown;
        }

        /// <summary>
        /// Applies MySQL provider-specific rules to classify a <see cref="MySqlException"/>.
        /// </summary>
        /// <param name="exception">
        /// MySQL connector exception from connection open, command execution, or pool acquisition. Must not be
        /// <see langword="null"/>; only called from <see cref="Classify"/> after an outer type match.
        /// </param>
        /// <returns>
        /// <see cref="AuthMySqlFailureReason.ConnectTimeout"/>, <see cref="AuthMySqlFailureReason.QueryTimeout"/>,
        /// <see cref="AuthMySqlFailureReason.PoolPressure"/>, or <see cref="AuthMySqlFailureReason.Unknown"/>.
        /// </returns>
        /// <remarks>
        /// <para><b>Decision order:</b></para>
        /// <list type="number">
        /// <item>
        /// <description>
        /// <see cref="MySqlErrorCode.UnableToConnectToHost"/> → <see cref="AuthMySqlFailureReason.ConnectTimeout"/>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Message contains <c>timeout</c> or <c>timed out</c> (case-insensitive) →
        /// <see cref="AuthMySqlFailureReason.ConnectTimeout"/> when the message also contains <c>connect</c>; otherwise
        /// <see cref="AuthMySqlFailureReason.QueryTimeout"/>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Message contains <c>pool</c> (case-insensitive) → <see cref="AuthMySqlFailureReason.PoolPressure"/>.
        /// </description>
        /// </item>
        /// <item><description>Otherwise → <see cref="AuthMySqlFailureReason.Unknown"/>.</description></item>
        /// </list>
        /// <para>
        /// Timeout keyword heuristics are evaluated before pool keywords. Messages matching both timeout and pool patterns
        /// are classified by the timeout branch.
        /// </para>
        /// <para>Does not walk <see cref="Exception.InnerException"/>; outer <see cref="Classify"/> handles wrapping.</para>
        /// </remarks>
        private static AuthMySqlFailureReason ClassifyMySqlException(MySqlException exception)
        {
            if (exception.ErrorCode == MySqlErrorCode.UnableToConnectToHost)
            {
                return AuthMySqlFailureReason.ConnectTimeout;
            }

            string message = exception.Message;
            return message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                ? message.Contains("connect", StringComparison.OrdinalIgnoreCase)
                    ? AuthMySqlFailureReason.ConnectTimeout
                    : AuthMySqlFailureReason.QueryTimeout
                : message.Contains("pool", StringComparison.OrdinalIgnoreCase)
                ? AuthMySqlFailureReason.PoolPressure
                : AuthMySqlFailureReason.Unknown;
        }
    }
}
