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
    /// Validates MySQL connectivity for NNTP authentication during host startup.
    /// </summary>
    /// <remarks>
    /// <para><b>Behavior:</b> Opens a connection and executes <c>SELECT 1</c>. Failure throws and prevents the host
    /// from accepting authentication traffic with an unreachable database.</para>
    /// </remarks>
    internal sealed partial class MySqlAuthConnectivityValidator : IHostedService
    {
        /// <summary>
        /// Startup connectivity probe timeout in seconds.
        /// </summary>
        private const int ConnectivityTimeoutSeconds = 5;

        /// <summary>
        /// Validated connection settings.
        /// </summary>
        private readonly MySqlAuthOptions _options;

        /// <summary>
        /// Logger for connectivity validation events.
        /// </summary>
        private readonly ILogger<MySqlAuthConnectivityValidator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlAuthConnectivityValidator"/> class.
        /// </summary>
        /// <param name="options">Validated MySQL authentication options.</param>
        /// <param name="logger">Logger instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="logger"/> is null.</exception>
        internal MySqlAuthConnectivityValidator(MySqlAuthOptions options, ILogger<MySqlAuthConnectivityValidator> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Starts the connectivity validation.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the connectivity validation fails.</exception>
        /// <remarks>
        /// The probe uses synchronous <see cref="MySqlConnection.Open"/> and <c>ExecuteScalar</c>; <paramref name="cancellationToken"/>
        /// is not observed. Wait time is bounded by connection-string <c>ConnectionTimeout</c> and
        /// <see cref="ConnectivityTimeoutSeconds"/>.
        /// </remarks>
        public Task StartAsync(CancellationToken cancellationToken)
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
        /// Stops the connectivity validation.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
