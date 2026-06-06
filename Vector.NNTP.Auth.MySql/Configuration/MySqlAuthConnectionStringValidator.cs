// <copyright file="MySqlAuthConnectionStringValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: startup validation for MainDB MySQL connection strings.

using MySqlConnector;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Auth.MySql.Configuration
{
    /// <summary>
    /// Validates MySQL connection strings used for NNTP authentication startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Production:</b> Connection strings should include explicit <c>ConnectionTimeout</c> and
    /// <c>DefaultCommandTimeout</c> values so authentication I/O is bounded under NNRPD burst load.
    /// </para>
    /// </remarks>
    internal static class MySqlAuthConnectionStringValidator
    {
        /// <summary>
        /// Validates that the connection string is present, parseable by <see cref="MySqlConnectionStringBuilder"/>, contains
        /// non-empty <c>Server</c> and <c>Database</c>, and does not contain placeholder credentials.
        /// </summary>
        /// <param name="connectionString">MySQL connection string for the <c>nntpusers</c> table.</param>
        /// <param name="paramName">Parameter name for exception messages.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="connectionString"/> is blank, malformed, missing server or database, contains a
        /// placeholder user name or password, or is not parseable as a MySQL connection string.
        /// </exception>
        internal static void ValidateOrThrow(string connectionString, string paramName = "connectionString")
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is required.", paramName);
            }

            MySqlConnectionStringBuilder builder;
            try
            {
                builder = new MySqlConnectionStringBuilder(connectionString);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Connection string is not a valid MySQL connection string.", paramName, ex);
            }

            if (string.IsNullOrWhiteSpace(builder.Server))
            {
                throw new ArgumentException("Connection string Server is required.", paramName);
            }

            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                throw new ArgumentException("Connection string Database is required.", paramName);
            }

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
