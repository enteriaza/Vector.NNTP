// <copyright file="AuthMySqlFailureClassifier.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: maps database exceptions to structured failure reasons for logs and metrics.

using System.IO;
using MySqlConnector;

namespace Vector.NNTP.Auth.MySql.Configuration
{
    /// <summary>
    /// Classifies backend exceptions from MySQL authentication I/O into stable <see cref="AuthMySqlFailureReason"/> values.
    /// </summary>
    internal static class AuthMySqlFailureClassifier
    {
        /// <summary>
        /// Classifies <paramref name="exception"/> for structured logging and diagnostics.
        /// </summary>
        /// <param name="exception">Exception raised during user lookup or validation I/O.</param>
        /// <returns>Stable failure reason for operators and metrics.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
        /// <remarks>
        /// Walks <see cref="Exception.InnerException"/> when the outer exception is not recognised. MySQL message heuristics
        /// distinguish connect timeouts, query timeouts, and pool pressure.
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
        /// Maps <see cref="MySqlException"/> error numbers and messages to failure reasons.
        /// </summary>
        /// <param name="exception">MySQL provider exception.</param>
        /// <returns>Classified failure reason.</returns>
        /// <remarks>
        /// Uses <see cref="MySqlException.ErrorCode"/> first, then case-insensitive substring checks on
        /// <see cref="Exception.Message"/> for timeout and pool keywords.
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
