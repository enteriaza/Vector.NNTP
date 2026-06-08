// <copyright file="MySqlAuthOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: validated MySQL connection settings for NNTP authentication services.

namespace Vector.NNTP.Auth.MySql.Configuration
{
    /// <summary>
    /// Immutable MySQL authentication settings constructed at host startup from a validated connection string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Central configuration object for the <c>Vector.NNTP.Auth.MySql</c> assembly. Registered as a singleton
    /// by <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/> (production hosts typically call
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuthFromHostConfiguration"/>, which reads
    /// <c>ConnectionStrings:MainDB</c>).
    /// </para>
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Records.MySqlUserRecordStore"/> — opens <see cref="ConnectionString"/> for <c>nntpusers</c> lookups.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="HostedServices.MySqlAuthConnectivityValidator"/> — fail-fast <c>SELECT 1</c> probe at host start.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Records.MySqlUserRecordCache"/> — receives <see cref="AuthCacheTtl"/> when constructed from DI.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Validation:</b> The constructor calls <see cref="MySqlAuthConnectionStringValidator.ValidateOrThrow"/> so
    /// malformed, incomplete, or placeholder connection strings fail during service registration rather than on first client
    /// authentication.
    /// </para>
    /// <para>
    /// <b>Production connection strings:</b> Include explicit <c>ConnectionTimeout</c> and <c>DefaultCommandTimeout</c> so
    /// connect and query waits are bounded under NNRPD burst load (see
    /// <see cref="MySqlAuthConnectionStringValidator"/> remarks).
    /// </para>
    /// <para>
    /// <b>Configuration surface:</b> Only the connection string is supplied by the host today; <see cref="AuthCacheTtl"/> is
    /// fixed at construction and is not bound from <c>appsettings</c>.
    /// </para>
    /// <para><b>Thread safety:</b> Immutable after construction; safe to read from concurrent session handlers.</para>
    /// </remarks>
    internal sealed class MySqlAuthOptions
    {
        /// <summary>
        /// Default time-to-live applied to <see cref="AuthCacheTtl"/> when options are constructed.
        /// </summary>
        /// <remarks>
        /// Ten seconds balances burst deduplication (many sessions authenticating with the same credentials) against
        /// minimising how long AES-protected <see cref="Records.MySqlUserRecord"/> snapshots remain in the
        /// <see cref="Records.MySqlUserRecordCache"/>.
        /// </remarks>
        private static readonly TimeSpan DefaultAuthCacheTtl = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Creates validated authentication options from a MySQL connection string.
        /// </summary>
        /// <param name="connectionString">
        /// Connection string for the <c>nntpusers</c> table (typically <c>ConnectionStrings:MainDB</c>). Must be non-blank,
        /// parseable by <c>MySqlConnector</c>, include <c>Server</c> and <c>Database</c>, and use real (non-placeholder)
        /// credentials.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="connectionString"/> fails <see cref="MySqlAuthConnectionStringValidator"/> checks.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Assigns <see cref="ConnectionString"/> to the validated input and sets <see cref="AuthCacheTtl"/> to
        /// <see cref="DefaultAuthCacheTtl"/>. Does not open a database connection.
        /// </para>
        /// <para>
        /// Invoked once per <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpMySqlAuth"/> call when the
        /// singleton is registered.
        /// </para>
        /// </remarks>
        internal MySqlAuthOptions(string connectionString)
        {
            MySqlAuthConnectionStringValidator.ValidateOrThrow(connectionString, nameof(connectionString));
            ConnectionString = connectionString;
            AuthCacheTtl = DefaultAuthCacheTtl;
        }

        /// <summary>
        /// Validated MySQL connection string used for authentication I/O.
        /// </summary>
        /// <value>
        /// The same string passed to the constructor after <see cref="MySqlAuthConnectionStringValidator"/> acceptance.
        /// </value>
        /// <remarks>
        /// <para>
        /// Targets the <c>MainDB</c> / <c>nntpusers</c> database in production hosts. Passed to
        /// <see cref="MySqlConnector.MySqlConnection"/> by <see cref="Records.MySqlUserRecordStore"/> and
        /// <see cref="HostedServices.MySqlAuthConnectivityValidator"/>.
        /// </para>
        /// <para>Immutable for the process lifetime; not reloaded when configuration files change.</para>
        /// </remarks>
        internal string ConnectionString { get; }

        /// <summary>
        /// Time-to-live for entries in the post-success authentication burst cache.
        /// </summary>
        /// <value>
        /// Currently always <see cref="DefaultAuthCacheTtl"/> (ten seconds). Not configurable from host configuration.
        /// </value>
        /// <remarks>
        /// <para>
        /// Fed to <see cref="Records.MySqlUserRecordCache"/> at DI registration. Entries expire by absolute UTC instant
        /// (<c>UtcNow + AuthCacheTtl</c> at insert); there is no count-based eviction.
        /// </para>
        /// <para>
        /// Only successful validations are cached; failed lookups and credential-store rejections never use this TTL.
        /// Distinct from per-exchange <see cref="Records.MySqlUserRecordSaslCache"/> staging during SASL setup.
        /// </para>
        /// </remarks>
        internal TimeSpan AuthCacheTtl { get; }
    }
}
