// <copyright file="MySqlAuthConnectionStringValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: startup validation for MainDB MySQL connection strings.

using MySqlConnector;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Validates MySQL connection strings used for NNTP authentication startup.
    /// </summary>
    internal static class MySqlAuthConnectionStringValidator
    {
        /// <summary>
        /// Validates that the connection string is present, parseable by <see cref="MySqlConnectionStringBuilder"/>, and
        /// does not contain placeholder credentials.
        /// </summary>
        /// <param name="connectionString">MySQL connection string for the <c>nntpusers</c> table.</param>
        /// <param name="paramName">Parameter name for exception messages.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="connectionString"/> is blank, contains a placeholder user name or password, or is
        /// not parseable as a MySQL connection string.
        /// </exception>
        internal static void ValidateOrThrow(string connectionString, string paramName = "connectionString")
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is required.", paramName);
            }

            MySqlConnectionStringBuilder builder = new(connectionString);
            if (CredentialPlaceholderDetector.IsPlaceholder(builder.UserID))
            {
                throw new ArgumentException(
                    "Connection string user name is missing or a template placeholder.",
                    paramName);
            }

            if (CredentialPlaceholderDetector.IsPlaceholder(builder.Password))
            {
                throw new ArgumentException(
                    "Connection string password is missing or a template placeholder.",
                    paramName);
            }
        }
    }
}
