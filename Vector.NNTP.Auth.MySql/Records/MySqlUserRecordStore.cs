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
    /// MySQL implementation of <see cref="INntpUserRecordStore"/> that materialises NNTP accounts from the
    /// <c>nntpusers</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Inner database store registered as a singleton by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>. Production hosts resolve
    /// <see cref="INntpUserRecordStore"/> as <see cref="CachingMySqlUserRecordStore"/>, which delegates cache misses to
    /// this type. <see cref="Credentials.MySqlNntpCredentialValidator"/> uses the async path;
    /// <see cref="Credentials.MySqlCramMd5CredentialStore"/> and <see cref="Credentials.MySqlScramCredentialStore"/> use
    /// synchronous <see cref="INntpUserRecordStore.TryGetUser"/> through the decorator.
    /// </para>
    /// <para>
    /// <b>Query:</b> Each lookup executes <see cref="UserLookupSql"/> with <c>@account_name</c> bound to the plaintext
    /// NNTP username. The <c>WHERE</c> clause compares the stored <c>account_name</c> column to <c>MD5(@account_name)</c>.
    /// Password decryption uses <c>AES_DECRYPT</c> with key material <c>UNHEX(SHA2(account_name, 256))</c> from the stored
    /// hash column, not from the bind parameter.
    /// </para>
    /// <para>
    /// <b>Observability:</b> Lookups emit lifecycle logs from <c>MySqlUserRecordStore.Logging.cs</c> (EventIds
    /// <c>400</c>–<c>403</c>), <see cref="AuthMySqlMetrics"/> counters/duration, and an
    /// <see cref="AuthMySqlTelemetry"/> <c>auth.mysql.user.lookup</c> span. Authentication cache hits never reach this
    /// store.
    /// </para>
    /// <para>
    /// <b>Thread safety:</b> Safe for concurrent NNTP session handlers. Each lookup opens a new
    /// <see cref="MySqlConnection"/> and relies on connector pooling for efficiency.
    /// </para>
    /// <para>
    /// <b>Timeouts:</b> <see cref="GetCommandTimeoutSeconds"/> reads <c>Default Command Timeout</c> from the connection
    /// string; when unset or zero, <see cref="DefaultCommandTimeoutSeconds"/> (<c>5</c>) is used for
    /// <see cref="MySqlCommand.CommandTimeout"/>. Production strings should set both connection and command timeouts
    /// explicitly.
    /// </para>
    /// </remarks>
    internal sealed partial class MySqlUserRecordStore : INntpUserRecordStore
    {
        /// <summary>
        /// Fallback ADO.NET command timeout in seconds when the connection string omits <c>Default Command Timeout</c>.
        /// </summary>
        /// <value><c>5</c> seconds.</value>
        private const int DefaultCommandTimeoutSeconds = 5;

        /// <summary>
        /// Parameterised SQL that loads one <c>nntpusers</c> row by MD5-hashed account name.
        /// </summary>
        /// <remarks>
        /// <para>Selected columns map to <see cref="MapUserRecord"/> ordinals:</para>
        /// <list type="number">
        /// <item><description><c>0</c> — decrypted <c>account_pass</c> (AES, empty string when null).</description></item>
        /// <item><description><c>1</c>–<c>4</c> — SCRAM salt, iterations, stored key, server key.</description></item>
        /// <item><description><c>5</c>–<c>6</c> — <c>allow_auth_plain</c>, <c>allow_auth_scram256</c> (<c>Y</c>/<c>N</c>).</description></item>
        /// <item><description><c>7</c>–<c>11</c> — account type and limit columns.</description></item>
        /// <item><description><c>12</c> — <c>is_enabled</c> (<c>Y</c>/<c>N</c>).</description></item>
        /// <item><description><c>13</c> — <c>customer_id</c> (string or GUID).</description></item>
        /// </list>
        /// <para>Uses <see cref="CommandBehavior.SingleRow"/>; at most one row is read per lookup.</para>
        /// </remarks>
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
        /// MySQL connection string for the <c>MainDB</c> / <c>nntpusers</c> database.
        /// </summary>
        /// <remarks>
        /// Captured from <see cref="MySqlAuthOptions.ConnectionString"/> at construction. Immutable for the store lifetime.
        /// </remarks>
        private readonly string _connectionString;

        /// <summary>
        /// Per-command timeout in seconds applied to every lookup <see cref="MySqlCommand"/>.
        /// </summary>
        /// <remarks>
        /// Computed once in the constructor by <see cref="GetCommandTimeoutSeconds"/> from <see cref="_connectionString"/>.
        /// </remarks>
        private readonly int _commandTimeoutSeconds;

        /// <summary>
        /// Category logger for lookup lifecycle events (EventIds <c>400</c>–<c>403</c>).
        /// </summary>
        private readonly ILogger<MySqlUserRecordStore> _logger;

        /// <summary>
        /// Metrics recorder for lookup outcomes and wall-clock duration.
        /// </summary>
        private readonly AuthMySqlMetrics _metrics;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlUserRecordStore"/> class.
        /// </summary>
        /// <param name="options">
        /// Validated MySQL authentication options supplying <see cref="MySqlAuthOptions.ConnectionString"/>. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="logger">Logger for lookup lifecycle events. Must not be <see langword="null"/>.</param>
        /// <param name="metrics">Shared Auth.MySql metrics singleton. Must not be <see langword="null"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="options"/>, <paramref name="logger"/>, or <paramref name="metrics"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Derives <see cref="_commandTimeoutSeconds"/> from the connection string before any lookup is served.
        /// </remarks>
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
        /// Synchronous <see cref="INntpUserRecordStore"/> entry point for account lookup.
        /// </summary>
        /// <param name="accountName">NNTP account name to look up. Must not be null or empty.</param>
        /// <returns>
        /// A materialised <see cref="MySqlUserRecord"/> when a row exists; otherwise <see langword="null"/> (not an error).
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="accountName"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Delegates to <see cref="ExecuteLookup"/> on the synchronous path. Used by SASL credential stores on protocol
        /// threads where the <see cref="INntpUserRecordStore"/> contract does not expose cancellation.
        /// </para>
        /// <para>
        /// MySQL, network, and unexpected mapper faults propagate after Error-level logging, <c>transient_failure</c>
        /// metrics, and optional trace error status. Does not catch and convert faults into <see langword="null"/>.
        /// </para>
        /// </remarks>
        MySqlUserRecord? INntpUserRecordStore.TryGetUser(string accountName)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountName);
            return ExecuteLookup(accountName, isAsync: false, cancellationToken: CancellationToken.None);
        }

        /// <summary>
        /// Asynchronous <see cref="INntpUserRecordStore"/> entry point for account lookup.
        /// </summary>
        /// <param name="accountName">NNTP account name to look up. Must not be null or empty.</param>
        /// <param name="cancellationToken">Cancellation token honoured during connection open and reader I/O.</param>
        /// <returns>
        /// A task producing a <see cref="MySqlUserRecord"/> when a row exists; otherwise <see langword="null"/>.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="accountName"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is signalled during async I/O (classified as
        /// <see cref="AuthMySqlFailureReason.Cancelled"/> in logs when caught as a general <see cref="Exception"/>).
        /// </exception>
        /// <remarks>
        /// Delegates to <see cref="ExecuteLookupAsync"/>. Preferred by
        /// <see cref="Credentials.MySqlNntpCredentialValidator"/> during AUTH finalization.
        /// </remarks>
        Task<MySqlUserRecord?> INntpUserRecordStore.TryGetUserAsync(string accountName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountName);
            return ExecuteLookupAsync(accountName, cancellationToken);
        }

        /// <summary>
        /// Performs a synchronous MySQL lookup with logging, metrics, tracing, and row mapping.
        /// </summary>
        /// <param name="accountName">Account name bound to <c>@account_name</c>.</param>
        /// <param name="isAsync">
        /// Passed to <see cref="UserLookupStarted"/> only (<see langword="false"/> on this code path).
        /// </param>
        /// <param name="cancellationToken">Ignored on the synchronous path; present for shared helper signature symmetry.</param>
        /// <returns>Materialised record or <see langword="null"/> when no row matches.</returns>
        /// <remarks>
        /// <para><b>Flow:</b> start span → log started → open connection → execute <see cref="UserLookupSql"/> → map row or
        /// return null → record metrics → always record duration in <c>finally</c>.</para>
        /// <para>Exceptions after logging are rethrown; duration is still recorded in <c>finally</c>.</para>
        /// </remarks>
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
        /// Performs an asynchronous MySQL lookup with logging, metrics, tracing, and row mapping.
        /// </summary>
        /// <param name="accountName">Account name bound to <c>@account_name</c>.</param>
        /// <param name="cancellationToken">Honoured by <c>OpenAsync</c>, <c>ExecuteReaderAsync</c>, and <c>ReadAsync</c>.</param>
        /// <returns>
        /// A task producing a materialised record or <see langword="null"/> when no row matches.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Mirrors <see cref="ExecuteLookup"/> but uses async ADO.NET APIs with <c>ConfigureAwait(false)</c> throughout.
        /// Logs <c>Async=true</c> via <see cref="UserLookupStarted"/>.
        /// </para>
        /// <para>Duration and outcome metrics follow the same rules as the synchronous path.</para>
        /// </remarks>
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
        /// Maps the current <see cref="MySqlDataReader"/> row to a <see cref="MySqlUserRecord"/>.
        /// </summary>
        /// <param name="reader">
        /// Reader positioned on the result row from <see cref="UserLookupSql"/>. Must not be null.
        /// </param>
        /// <param name="accountName">
        /// Plaintext account name from the caller (stored on the record even though the query keys by MD5 hash).
        /// </param>
        /// <returns>Immutable in-memory user record for authentication and session policy construction.</returns>
        /// <remarks>
        /// <para><b>Flag columns:</b> <c>allow_auth_plain</c>, <c>allow_auth_scram256</c>, and <c>is_enabled</c> are
        /// true only when the column value equals <c>Y</c> (case-insensitive).</para>
        /// <para><b>Defaults for null numerics:</b> SCRAM iterations and limit columns default to <c>0</c>;
        /// <see cref="MySqlUserRecord.AccountType"/> defaults to <c>'R'</c> when null.</para>
        /// <para><b>Password:</b> Null decrypted password becomes <see cref="string.Empty"/> on the record.</para>
        /// </remarks>
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
        /// Reads a binary column into <see cref="ReadOnlyMemory{T}"/> without copying when the provider returns a
        /// <c>byte[]</c> buffer.
        /// </summary>
        /// <param name="reader">Active row reader. Must not be <see langword="null"/>.</param>
        /// <param name="ordinal">Zero-based column ordinal (SCRAM salt/key columns in <see cref="UserLookupSql"/>).</param>
        /// <returns>
        /// Column bytes wrapped in <see cref="ReadOnlyMemory{T}"/>, or <see cref="ReadOnlyMemory{T}.Empty"/> when the column
        /// is SQL <c>NULL</c> or zero-length.
        /// </returns>
        /// <remarks>Never throws for null or empty columns; does not validate <paramref name="ordinal"/> range.</remarks>
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
        /// Parses <c>Default Command Timeout</c> from a MySQL connection string for lookup commands.
        /// </summary>
        /// <param name="connectionString">Connection string from <see cref="MySqlAuthOptions"/>. Must be parseable.</param>
        /// <returns>
        /// Positive <see cref="MySqlConnectionStringBuilder.DefaultCommandTimeout"/> when set; otherwise
        /// <see cref="DefaultCommandTimeoutSeconds"/>.
        /// </returns>
        /// <remarks>
        /// Does not read connection open timeout (<c>Connection Timeout</c>); that remains a connector concern during
        /// <see cref="MySqlConnection.Open"/> / <c>OpenAsync</c>.
        /// </remarks>
        private static int GetCommandTimeoutSeconds(string connectionString)
        {
            MySqlConnectionStringBuilder builder = new(connectionString);

            return builder.DefaultCommandTimeout > 0
                ? (int)builder.DefaultCommandTimeout
                : DefaultCommandTimeoutSeconds;
        }

        /// <summary>
        /// Normalises the <c>customer_id</c> column to a stable string for <see cref="MySqlUserRecord.CustomerId"/>.
        /// </summary>
        /// <param name="reader">Active row reader.</param>
        /// <param name="ordinal">Ordinal of <c>customer_id</c> (<c>13</c> in <see cref="UserLookupSql"/>).</param>
        /// <returns>
        /// String form of the column, or <see cref="string.Empty"/> when SQL <c>NULL</c>. GUID values use
        /// <c>D</c> format with invariant culture.
        /// </returns>
        /// <remarks>
        /// Uses <see cref="MySqlDataReader.GetValue"/> so both string and uniqueidentifier column types map consistently
        /// across schema variants.
        /// </remarks>
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
