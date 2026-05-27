// <copyright file="MySqlUserRecordStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: MySQL-backed implementation of the NNTP user record store.

using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
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
    /// WHERE account_name = @account_name;
    /// </code>
    /// <para>
    /// <b>Thread safety:</b> This type is safe for concurrent use and is registered as a singleton. It opens a new
    /// <see cref="MySqlConnection"/> for each lookup and relies on the underlying ADO.NET pooling for efficiency.
    /// </para>
    /// </remarks>
    internal sealed class MySqlUserRecordStore : INntpUserRecordStore
    {
        private const string UserLookupSql =
            "SELECT CAST(AES_DECRYPT(account_pass, UNHEX(SHA2(account_name, 256))) AS CHAR) AS account_pass, " +
            "account_type, account_rate_limit, account_byte_limit, account_session_limit, account_srcip_limit, " +
            "is_enabled, customer_id FROM nntpusers WHERE account_name = MD5(@account_name)";

        private readonly IOptions<NntpUsersOptions> _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlUserRecordStore"/> class.
        /// </summary>
        /// <param name="options">NNTP user store options.</param>
        public MySqlUserRecordStore(IOptions<NntpUsersOptions> options)
        {
            this._options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public async Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountName);

            NntpUsersOptions current = this._options.Value;
            using MySqlConnection connection = new MySqlConnection(current.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using MySqlCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = UserLookupSql;
            command.CommandTimeout = current.CommandTimeout;
            _ = command.Parameters.AddWithValue("@account_name", accountName);

            using MySqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            string password = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            char accountType = reader.IsDBNull(1) ? 'R' : reader.GetChar(1);
            int rateLimit = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            long byteLimit = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);
            int sessionLimit = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            int srcIpLimit = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
            bool isEnabled = !reader.IsDBNull(6) &&
                string.Equals(reader.GetString(6), "Y", StringComparison.OrdinalIgnoreCase);
            string customerId = ReadCustomerId(reader, 7);

            return new MySqlUserRecord(
                accountName,
                password,
                accountType,
                rateLimit,
                byteLimit,
                sessionLimit,
                srcIpLimit,
                isEnabled,
                customerId);
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
