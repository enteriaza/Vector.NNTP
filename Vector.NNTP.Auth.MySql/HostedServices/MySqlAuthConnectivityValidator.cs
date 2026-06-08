// <copyright file="MySqlAuthConnectivityValidator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: fail-fast MySQL connectivity check at host startup.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Vector.NNTP.Auth.MySql.Configuration;

namespace Vector.NNTP.Auth.MySql.HostedServices
{
    /// <summary>
    /// Fail-fast hosted service that proves the auth MySQL database is reachable before NNTP credential handling starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Registered by <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/> as an
    /// <see cref="IHostedService"/> that runs once during host startup. Uses the same
    /// <see cref="MySqlAuthOptions.ConnectionString"/> as <see cref="Records.MySqlUserRecordStore"/> but executes only
    /// <c>SELECT 1</c> — it does not query <c>nntpusers</c> or validate schema.
    /// </para>
    /// <para>
    /// <b>Success path:</b> Opens a <see cref="MySqlConnection"/>, runs <c>SELECT 1</c> with a fixed command timeout, logs
    /// EventId <c>110</c> via <c>MySqlAuthConnectivityValidator.Logging.cs</c>, and returns
    /// <see cref="Task.CompletedTask"/> so the host can continue starting listeners and authentication services.
    /// </para>
    /// <para>
    /// <b>Failure path:</b> Any exception from connect or execute is logged at EventId <c>111</c>, wrapped in
    /// <see cref="InvalidOperationException"/>, and rethrown so the generic host aborts startup rather than accepting AUTH
    /// traffic against an unreachable database.
    /// </para>
    /// <para>
    /// <b>Timeouts:</b> Connection open honours <c>Connection Timeout</c> from the connection string. The probe command uses
    /// <see cref="ConnectivityTimeoutSeconds"/> (<c>5</c>) for <see cref="MySqlCommand.CommandTimeout"/> only; it does not
    /// read <c>Default Command Timeout</c> from the string (unlike per-lookup commands in the record store).
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> <see cref="IHostedService.StopAsync"/> is a no-op; there is no periodic
    /// re-validation after startup.
    /// </para>
    /// </remarks>
    internal sealed partial class MySqlAuthConnectivityValidator : IHostedService
    {
        /// <summary>
        /// Command timeout in seconds applied to the startup <c>SELECT 1</c> probe.
        /// </summary>
        /// <value><c>5</c> seconds.</value>
        /// <remarks>
        /// Bound independently of <c>Default Command Timeout</c> in the connection string so startup fail-fast behavior is
        /// predictable even when lookup commands use a different default.
        /// </remarks>
        private const int ConnectivityTimeoutSeconds = 5;

        /// <summary>
        /// Validated authentication options supplying the MySQL connection string for the probe.
        /// </summary>
        /// <remarks>
        /// Captured at construction from DI; immutable for the hosted-service lifetime.
        /// </remarks>
        private readonly MySqlAuthOptions _options;

        /// <summary>
        /// Category logger for startup connectivity success and failure events (EventIds <c>110</c>–<c>111</c>).
        /// </summary>
        /// <remarks>Passed to source-generated helpers in the logging partial.</remarks>
        private readonly ILogger<MySqlAuthConnectivityValidator> _logger;

        /// <summary>
        /// Creates a startup connectivity validator bound to validated auth database settings.
        /// </summary>
        /// <param name="options">
        /// Singleton <see cref="MySqlAuthOptions"/> from <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <param name="logger">
        /// Logger for <see cref="MySqlAuthConnectivityValidator"/>. Must not be <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="options"/> or <paramref name="logger"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>Does not open a connection; the probe runs on the first <c>StartAsync</c> invocation.</remarks>
        internal MySqlAuthConnectivityValidator(MySqlAuthOptions options, ILogger<MySqlAuthConnectivityValidator> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Runs the synchronous <c>SELECT 1</c> connectivity probe during host startup.
        /// </summary>
        /// <param name="cancellationToken">
        /// Host shutdown token from the generic host. Not observed by this implementation; the probe always runs to
        /// completion or failure synchronously inside <c>StartAsync</c>.
        /// </param>
        /// <returns><see cref="Task.CompletedTask"/> when the probe succeeds.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when connection open or <c>SELECT 1</c> execution fails. The inner exception preserves the original
        /// <see cref="MySqlConnector"/> or network fault after EventId <c>111</c> is logged.
        /// </exception>
        /// <remarks>
        /// <para><b>Flow:</b></para>
        /// <list type="number">
        /// <item><description>Construct <see cref="MySqlConnection"/> from <see cref="MySqlAuthOptions.ConnectionString"/>.</description></item>
        /// <item><description><see cref="MySqlConnection.Open"/> (connection-string <c>Connection Timeout</c> applies).</description></item>
        /// <item><description>Execute <c>SELECT 1</c> via <c>ExecuteScalar</c> with <see cref="ConnectivityTimeoutSeconds"/>.</description></item>
        /// <item><description>Log success (EventId <c>110</c>) or failure (EventId <c>111</c>) and return or throw.</description></item>
        /// </list>
        /// <para>
        /// Implements <see cref="IHostedService.StartAsync"/>. All exceptions are caught,
        /// logged, and rethrown as <see cref="InvalidOperationException"/>; none are swallowed.
        /// </para>
        /// </remarks>
        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using MySqlConnection connection = new(_options.ConnectionString);
                connection.Open();
                using MySqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.CommandTimeout = ConnectivityTimeoutSeconds;
                _ = command.ExecuteScalar();
                ConnectivityValidationSucceeded(_logger);
            }
            catch (Exception ex)
            {
                ConnectivityValidationFailed(_logger, ex);
                throw new InvalidOperationException(
                    "MySQL authentication connectivity validation failed. See inner exception for details.",
                    ex);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// No-op hosted-service shutdown hook.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown token (ignored).</param>
        /// <returns><see cref="Task.CompletedTask"/> immediately.</returns>
        /// <remarks>
        /// Connectivity validation runs only during
        /// <see cref="IHostedService.StartAsync"/>. No connections are held open after the
        /// probe completes.
        /// </remarks>
        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
