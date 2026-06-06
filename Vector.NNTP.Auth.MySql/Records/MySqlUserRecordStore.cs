// <copyright file="MySqlUserRecordStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// Control path: MySQL-backed implementation of the NNTP user record store.

using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Auth.MySql.Telemetry;

namespace Vector.NNTP.Auth.MySql.Records
{
    /// <summary>
    /// Retrieves NNTP user records from a MySQL <c>nntpusers</c> table using a parameterised query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Query:</b> This store issues the following SQL statement against the configured connection.  The
    /// <c>@account_name</c> parameter is bound to the plaintext NNTP username; the server-side
    /// <c>account_name</c> column stores <c>MD5(@account_name)</c> and supplies the AES key material via
    /// <c>SHA2(account_name, 256)</c> (the stored hash, not the bind parameter).
    /// </para>
    /// <para>
    /// <b>Thread safety:</b> This type is safe for concurrent use and is registered as a singleton. It opens a new
    /// <see cref="MySqlConnection"/> for each lookup and relies on the underlying ADO.NET pooling for efficiency.
    /// </para>
    /// <para>
    /// <b>Timeouts:</b> Production connection strings should set <c>ConnectionTimeout</c> and
    /// <c>DefaultCommandTimeout</c> explicitly. When omitted, command timeout defaults to five seconds.
    /// </para>
    /// </remarks>
    internal sealed partial class MySqlUserRecordStore : INntpUserRecordStore
    {
        /// <summary>
        /// Default ADO.NET command timeout in seconds when the connection string does not specify one.
        /// </summary>
        private const int DefaultCommandTimeoutSeconds = 5;

        /// <summary>
        /// SQL statement to lookup a user record by account name.
        /// </summary>
        private const string UserLookupSql =
            "SELECT " +
            "CAST(AES_DECRYPT(account_pass, UNHEX(SHA2(account_name, 256))) AS CHAR) AS account_pass, " +
            "scram_salt, scram_iterations, scram_stored_key, scram_server_key, " +
            "allow_auth_plain, allow_auth_scram256, " +
            "account_type, account_rate_limit, account_byte_limit, account_session_limit, account_srcip_limit, " +
            "is_enabled, customer_id " +
            "FROM nntpusers " +
            "WHERE account_name = MD5(@account_name)";

        /// <summary>
        /// MySQL connection string for the <c>nntpusers</c> table.
        /// </summary>
        private readonly string _connectionString;

        /// <summary>
        /// Cached command timeout in seconds, derived from the connection string.
        /// </summary>
        private readonly int _commandTimeoutSeconds;

        /// <summary>
        /// Logger for lookup lifecycle events.
        /// </summary>
        private readonly ILogger<MySqlUserRecordStore> _logger;

        /// <summary>
        /// Metrics for lookup outcomes and duration.
        /// </summary>
        private readonly AuthMySqlMetrics _metrics;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlUserRecordStore"/> class.
        /// </summary>
        /// <param name="options">Validated MySQL authentication connection settings.</param>
        /// <param name="logger">Logger for lookup lifecycle events.</param>
        /// <param name="metrics">Metrics for lookup outcomes and duration.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        internal MySqlUserRecordStore(
            MySqlAuthOptions options,
            ILogger<MySqlUserRecordStore> logger,
            AuthMySqlMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(options);
            _connectionString = options.ConnectionString;
            _commandTimeoutSeconds = GetCommandTimeoutSeconds(options.ConnectionString);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }

        /// <summary>
        /// Tries to get a user record by account name.
        /// </summary>
        /// <param name="accountName">Account name to lookup.</param>
        /// <returns>User record or <see langword="null"/> when not found.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountName"/> is null or empty.</exception>
        /// <remarks>
        /// MySQL and transport exceptions propagate after logging and metrics recording.
        /// </remarks>
        public MySqlUserRecord? TryGetUser(string accountName)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountName);
            return ExecuteLookup(accountName, isAsync: false, cancellationToken: CancellationToken.None);
        }

        /// <summary>
        /// Tries to get a user record by account name asynchronously.
        /// </summary>
        /// <param name="accountName">Account name to lookup.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>User record or <see langword="null"/> when not found.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountName"/> is null or empty.</exception>
        /// <remarks>
        /// MySQL and transport exceptions propagate after logging and metrics recording.
        /// <see cref="OperationCanceledException"/> propagates when <paramref name="cancellationToken"/> is signalled.
        /// </remarks>
        public Task<MySqlUserRecord?> TryGetUserAsync(string accountName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountName);
            return ExecuteLookupAsync(accountName, cancellationToken);
        }

        /// <summary>
        /// Executes a synchronous lookup with logging, metrics, and activity instrumentation.
        /// </summary>
        /// <param name="accountName">Account name to lookup.</param>
        /// <param name="isAsync">Whether the caller is the async entry point (for log context only).</param>
        /// <param name="cancellationToken">Cancellation token (unused on sync path).</param>
        /// <returns>User record or <see langword="null"/> when not found.</returns>
        private MySqlUserRecord? ExecuteLookup(string accountName, bool isAsync, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            using Activity? activity = AuthMySqlTelemetry.ActivitySource.StartActivity("auth.mysql.user.lookup", ActivityKind.Client);
            UserLookupStarted(_logger, accountName, isAsync);
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                using MySqlConnection connection = new(_connectionString);
                connection.Open();

                using MySqlCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = UserLookupSql;
                command.CommandTimeout = _commandTimeoutSeconds;
                _ = command.Parameters.AddWithValue("@account_name", accountName);

                using MySqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleRow);
                if (!reader.Read())
                {
                    UserLookupNotFound(_logger, accountName);
                    _metrics.RecordLookup("not_found");
                    return null;
                }

                MySqlUserRecord record = MapUserRecord(reader, accountName);
                UserLookupSucceeded(_logger, accountName);
                _metrics.RecordLookup("found");
                return record;
            }
            catch (Exception ex)
            {
                AuthMySqlFailureReason reason = AuthMySqlFailureClassifier.Classify(ex);
                UserLookupFailed(_logger, ex, accountName, reason);
                _metrics.RecordLookup("transient_failure");
                if (activity is not null)
                {
                    _ = activity.SetStatus(ActivityStatusCode.Error, reason.ToString());
                }

                throw;
            }
            finally
            {
                stopwatch.Stop();
                _metrics.RecordLookupDuration(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Executes an asynchronous lookup with logging, metrics, and activity instrumentation.
        /// </summary>
        /// <param name="accountName">Account name to lookup.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>User record task producing <see langword="null"/> when not found.</returns>
        private async Task<MySqlUserRecord?> ExecuteLookupAsync(string accountName, CancellationToken cancellationToken)
        {
            using Activity? activity = AuthMySqlTelemetry.ActivitySource.StartActivity("auth.mysql.user.lookup", ActivityKind.Client);
            UserLookupStarted(_logger, accountName, isAsync: true);
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                using MySqlConnection connection = new(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using MySqlCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = UserLookupSql;
                command.CommandTimeout = _commandTimeoutSeconds;
                _ = command.Parameters.AddWithValue("@account_name", accountName);

                using MySqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    UserLookupNotFound(_logger, accountName);
                    _metrics.RecordLookup("not_found");
                    return null;
                }

                MySqlUserRecord record = MapUserRecord(reader, accountName);
                UserLookupSucceeded(_logger, accountName);
                _metrics.RecordLookup("found");
                return record;
            }
            catch (Exception ex)
            {
                AuthMySqlFailureReason reason = AuthMySqlFailureClassifier.Classify(ex);
                UserLookupFailed(_logger, ex, accountName, reason);
                _metrics.RecordLookup("transient_failure");
                if (activity is not null)
                {
                    _ = activity.SetStatus(ActivityStatusCode.Error, reason.ToString());
                }

                throw;
            }
            finally
            {
                stopwatch.Stop();
                _metrics.RecordLookupDuration(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Maps the current reader row to a <see cref="MySqlUserRecord"/>.
        /// </summary>
        /// <param name="reader">Active row reader positioned on a result row.</param>
        /// <param name="accountName">Account name used for the lookup bind parameter.</param>
        /// <returns>Materialised user record.</returns>
        private static MySqlUserRecord MapUserRecord(MySqlDataReader reader, string accountName)
        {
            string password = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            ReadOnlyMemory<byte> scramSalt = ReadBinaryColumn(reader, 1);
            int scramIterations = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            ReadOnlyMemory<byte> scramStoredKey = ReadBinaryColumn(reader, 3);
            ReadOnlyMemory<byte> scramServerKey = ReadBinaryColumn(reader, 4);

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
        /// Reads a binary column as a <see cref="ReadOnlyMemory{T}"/> wrapper over the provider-materialised
        /// <c>byte[]</c>.
        /// </summary>
        /// <param name="reader">Active row reader.</param>
        /// <param name="ordinal">Column ordinal.</param>
        /// <returns>Column bytes, or empty when the column is null or zero-length.</returns>
        private static ReadOnlyMemory<byte> ReadBinaryColumn(MySqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            byte[] value = reader.GetFieldValue<byte[]>(ordinal);
            return value.Length == 0 ? ReadOnlyMemory<byte>.Empty : value;
        }

        /// <summary>
        /// Extracts a stable command timeout from the connection string for ADO.NET command execution.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <returns>Command timeout in seconds.</returns>
        private static int GetCommandTimeoutSeconds(string connectionString)
        {
            MySqlConnectionStringBuilder builder = new(connectionString);

            return builder.DefaultCommandTimeout > 0
                ? (int)builder.DefaultCommandTimeout
                : DefaultCommandTimeoutSeconds;
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
