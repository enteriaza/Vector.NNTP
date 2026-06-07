// <copyright file="MySqlAuthOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: validated MySQL connection settings for NNTP authentication services.

namespace Vector.NNTP.Auth.MySql.Configuration
{
    /// <summary>
    /// Validated MySQL connection settings shared by MySQL-backed NNTP authentication services.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as a singleton by <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/>.
    /// </para>
    /// <para>
    /// <b>Production connection strings:</b> Set <c>ConnectionTimeout</c> and <c>DefaultCommandTimeout</c> explicitly
    /// so connect and query waits are bounded under load.
    /// </para>
    /// </remarks>
    internal sealed class MySqlAuthOptions
    {
        /// <summary>
        /// Default successful-authentication cache TTL applied when constructing options from a connection string.
        /// </summary>
        /// <remarks>
        /// Ten seconds balances burst deduplication against minimising cleartext credential retention in memory.
        /// </remarks>
        private static readonly TimeSpan DefaultAuthCacheTtl = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlAuthOptions"/> class.
        /// </summary>
        /// <param name="connectionString">MySQL connection string for the <c>nntpusers</c> table.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is invalid.</exception>
        internal MySqlAuthOptions(string connectionString)
        {
            MySqlAuthConnectionStringValidator.ValidateOrThrow(connectionString, nameof(connectionString));
            ConnectionString = connectionString;
            AuthCacheTtl = DefaultAuthCacheTtl;
        }

        /// <summary>
        /// Gets the validated MySQL connection string for the <c>nntpusers</c> table.
        /// </summary>
        internal string ConnectionString { get; }

        /// <summary>
        /// Gets the time-to-live for successful-authentication cache entries.
        /// </summary>
        /// <remarks>
        /// Defaults to ten seconds. Entries expire solely by elapsed time so concurrent sessions authenticating
        /// together share one MySQL lookup without retaining credentials in memory beyond that window.
        /// </remarks>
        internal TimeSpan AuthCacheTtl { get; }
    }
}
