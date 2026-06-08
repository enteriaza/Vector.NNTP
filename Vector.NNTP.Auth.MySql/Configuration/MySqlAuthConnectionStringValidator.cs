// <copyright file="MySqlAuthConnectionStringValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: startup validation for MainDB MySQL connection strings.

using MySqlConnector;
using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Auth.MySql.Configuration
{
    /// <summary>
    /// Fail-fast validation for MySQL connection strings used by NNTP authentication services.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Guards <see cref="MySqlAuthOptions"/> construction so hosts never start with an unusable
    /// <c>ConnectionStrings:MainDB</c> value. Invoked from <see cref="MySqlAuthOptions"/> during
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/> registration — before
    /// <see cref="HostedServices.MySqlAuthConnectivityValidator"/> or <see cref="Records.MySqlUserRecordStore"/> open a
    /// connection.
    /// </para>
    /// <para><b>Checks performed:</b></para>
    /// <list type="number">
    /// <item><description>Non-blank connection string.</description></item>
    /// <item><description>Parseable by <see cref="MySqlConnectionStringBuilder"/>.</description></item>
    /// <item><description>Non-empty <c>Server</c> and <c>Database</c> builder properties.</description></item>
    /// <item>
    /// <description>
    /// Non-placeholder <c>User ID</c> and <c>Password</c> via <see cref="CredentialPlaceholderDetector"/> (empty,
    /// whitespace, and tokens such as <c>changeme</c> from <see cref="CredentialPlaceholderDetector.CommonPlaceholders"/>).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Not validated here:</b> Network reachability, schema presence, and timeout values. Production connection strings
    /// should still include explicit <c>ConnectionTimeout</c> and <c>DefaultCommandTimeout</c> so authentication I/O is
    /// bounded under NNRPD burst load; missing timeouts are not rejected by this type.
    /// </para>
    /// <para>
    /// <b>Failure model:</b> Every rejection throws <see cref="ArgumentException"/> with a caller-supplied parameter name on
    /// <see cref="ArgumentException.ParamName"/> (see <see cref="ValidateOrThrow"/>). Malformed strings wrap the underlying
    /// parser exception as the inner exception.
    /// </para>
    /// <para><b>Thread safety:</b> Stateless static helpers; safe under concurrent host startup (not expected in practice).</para>
    /// </remarks>
    internal static class MySqlAuthConnectionStringValidator
    {
        /// <summary>
        /// Validates a MySQL connection string for NNTP authentication or throws.
        /// </summary>
        /// <param name="connectionString">
        /// Candidate connection string for the <c>nntpusers</c> table (typically <c>ConnectionStrings:MainDB</c>). Must not be
        /// blank or whitespace-only.
        /// </param>
        /// <param name="paramName">
        /// Name reported on thrown <see cref="ArgumentException.ParamName"/> so callers can distinguish the failing
        /// configuration key. Defaults to <c>connectionString</c>.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="connectionString"/> is blank (<c>Connection string is required.</c>), not parseable
        /// (<c>Connection string is not a valid MySQL connection string.</c>), missing <c>Server</c> or <c>Database</c>,
        /// or when <c>User ID</c> or <c>Password</c> is empty or matches a <see cref="CredentialPlaceholderDetector"/>
        /// placeholder.
        /// </exception>
        /// <remarks>
        /// <para><b>Flow:</b></para>
        /// <list type="number">
        /// <item><description>Reject null, empty, or whitespace-only <paramref name="connectionString"/>.</description></item>
        /// <item><description>Parse with <see cref="MySqlConnectionStringBuilder"/>; wrap parse failures.</description></item>
        /// <item><description>Require <see cref="MySqlConnectionStringBuilder.Server"/> and <see cref="MySqlConnectionStringBuilder.Database"/>.</description></item>
        /// <item><description>Reject placeholder <see cref="MySqlConnectionStringBuilder.UserID"/>.</description></item>
        /// <item><description>Reject placeholder <see cref="MySqlConnectionStringBuilder.Password"/>.</description></item>
        /// </list>
        /// <para>
        /// Returns normally when all checks pass. Does not open a <see cref="MySqlConnection"/> or execute SQL.
        /// </para>
        /// </remarks>
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
