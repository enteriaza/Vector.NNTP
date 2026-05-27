// <copyright file="MySqlUserRecordStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// Control path: MySQL-backed implementation of the NNTP user record store.

using System.Data;
using System.Globalization;
using MySqlConnector;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Retrieves NNTP user records from a MySQL <c>nntpusers</c> table using a parameterised query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Query:</b> This store issues the following SQL statement against the configured connection:
    /// </para>
    /// <code>
    /// SELECT
    ///   CAST(AES_DECRYPT(account_pass, UNHEX(SHA2(@account_name, 256))) AS CHAR) AS account_pass,
    ///   account_type,
    ///   account_rate_limit,
    ///   account_byte_limit,
    ///   account_session_limit,
    ///   account_srcip_limit,
    ///   is_enabled,
    ///   customer_id
    /// FROM nntpusers
    /// WHERE account_name = MD5(@account_name);
    /// </code>
    /// <para>
    /// <b>Thread safety:</b> This type is safe for concurrent use and is registered as a singleton. It opens a new
    /// <see cref="MySqlConnection"/> for each lookup and relies on the underlying ADO.NET pooling for efficiency.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="MySqlUserRecordStore"/> class.
    /// </remarks>
    /// <param name="connectionString">MySQL connection string for the <c>nntpusers</c> table.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is null or whitespace.</exception>
    internal sealed class MySqlUserRecordStore(string connectionString) : INntpUserRecordStore
    {
        /// <summary>
        /// SQL statement to lookup a user record by account name.
        /// </summary>
        private const string UserLookupSql =
            "SELECT " +
            "CAST(AES_DECRYPT(account_pass, UNHEX(SHA2(@account_name, 256))) AS CHAR) AS account_pass, " +
            "scram_salt, scram_iterations, scram_stored_key, scram_server_key, " +
            "allow_auth_plain, allow_auth_scram256, " +
            "account_type, account_rate_limit, account_byte_limit, account_session_limit, account_srcip_limit, " +
            "is_enabled, customer_id " +
            "FROM nntpusers " +
            "WHERE account_name = MD5(@account_name)";

        /// <summary>
        /// MySQL connection string for the <c>nntpusers</c> table.
        /// </summary>
        private readonly string _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("Connection string is required.", nameof(connectionString))
            : connectionString;

        /// <summary>
        /// Cached command timeout in seconds, derived from the connection string.
        /// </summary>
        private readonly int _commandTimeoutSeconds = GetCommandTimeoutSeconds(connectionString);

        /// <summary>
        /// Tries to get a user record by account name.
        /// </summary>
        /// <param name="accountName">Account name to lookup.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A <see cref="Task{TResult}"/> that completes with the user record, or <see langword="null"/> when no such
        /// account exists in the backing store.
        /// </returns>
        public async Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountName);

            using MySqlConnection connection = new(this._connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using MySqlCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = UserLookupSql;
            command.CommandTimeout = this._commandTimeoutSeconds;
            _ = command.Parameters.AddWithValue("@account_name", accountName);

            using MySqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            string password = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            ReadOnlyMemory<byte> scramSalt = reader.IsDBNull(1) ? ReadOnlyMemory<byte>.Empty : reader.GetFieldValue<byte[]>(1);
            int scramIterations = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            ReadOnlyMemory<byte> scramStoredKey = reader.IsDBNull(3) ? ReadOnlyMemory<byte>.Empty : reader.GetFieldValue<byte[]>(3);
            ReadOnlyMemory<byte> scramServerKey = reader.IsDBNull(4) ? ReadOnlyMemory<byte>.Empty : reader.GetFieldValue<byte[]>(4);

            bool allowAuthPlain = !reader.IsDBNull(5) &&
                string.Equals(reader.GetString(5), "Y", StringComparison.OrdinalIgnoreCase);
            bool allowAuthScram256 = !reader.IsDBNull(6) &&
                string.Equals(reader.GetString(6), "Y", StringComparison.OrdinalIgnoreCase);

            char accountType = reader.IsDBNull(7) ? 'R' : reader.GetChar(7);
            int rateLimit = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
            long byteLimit = reader.IsDBNull(9) ? 0L : reader.GetInt64(9);
            int sessionLimit = reader.IsDBNull(10) ? 0 : reader.GetInt32(10);
            int srcIpLimit = reader.IsDBNull(11) ? 0 : reader.GetInt32(11);
            bool isEnabled = !reader.IsDBNull(12) &&
                string.Equals(reader.GetString(12), "Y", StringComparison.OrdinalIgnoreCase);
            string customerId = ReadCustomerId(reader, 13);

            return new MySqlUserRecord(
                accountName,
                password,
                allowAuthPlain,
                allowAuthScram256,
                scramSalt,
                scramIterations,
                scramStoredKey,
                scramServerKey,
                accountType,
                rateLimit,
                byteLimit,
                sessionLimit,
                srcIpLimit,
                isEnabled,
                customerId);
        }

        /// <summary>
        /// Extracts a stable command timeout from the connection string for ADO.NET command execution.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <returns>Command timeout in seconds.</returns>
        private static int GetCommandTimeoutSeconds(string connectionString)
        {
            MySqlConnectionStringBuilder builder = new(connectionString);

            if (builder.TryGetValue("DefaultCommandTimeout", out object? value) &&
                value is not null &&
                int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) &&
                seconds > 0)
            {
                return seconds;
            }

            return 5;
        }

        /// <summary>
        /// Reads the <c>customer_id</c> column as a stable string regardless of provider-side CLR type mapping.
        /// </summary>
        /// <param name="reader">Active row reader.</param>
        /// <param name="ordinal">Column ordinal for <c>customer_id</c>.</param>
        /// <returns>Customer identifier string, or empty when the column is null.</returns>
        private static string ReadCustomerId(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
            {
                return string.Empty;
            }

            object value = reader.GetValue(ordinal);
            return value switch
            {
                string text => text,
                Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            };
        }
    }
}
