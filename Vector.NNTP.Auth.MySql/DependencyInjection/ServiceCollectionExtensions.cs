// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ServiceCollectionExtensions.cs -- DI entry points for MySQL-backed NNTP authentication.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Auth.MySql.Configuration;
using Vector.NNTP.Auth.MySql.Credentials;
using Vector.NNTP.Auth.MySql.HostedServices;
using Vector.NNTP.Auth.MySql.Records;
using Vector.NNTP.Auth.MySql.Telemetry;
using Vector.NNTP.Session.Accounts;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Auth.MySql.DependencyInjection
{
    /// <summary>
    /// Dependency-injection entry points that wire MySQL-backed NNTP authentication into a generic host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Assembly role:</b> Replaces development credential and SASL store stubs from
    /// <c>Vector.NNTP.Sockets</c> with production implementations backed by the <c>nntpusers</c> table. NNRPD and NNTPD
    /// hosts call <see cref="AddNntpMySqlAuthFromHostConfiguration"/> during startup (see
    /// <c>Vector.NNTP.NNRPD.Program</c> and <c>Vector.NNTP.NNTPD.Program</c>).
    /// </para>
    /// <para>
    /// <b>Ordering:</b> May run before or after <c>AddNntpSocketsReader</c> / <c>AddNntpSocketsTransit</c>.
    /// <see cref="AddNntpMySqlAuth"/> removes prior auth-related service descriptors so
    /// MySQL implementations win when socket registration added development stubs first. When MySQL auth is registered first,
    /// later <c>TryAdd</c> stub registration does not override production services.
    /// </para>
    /// <para>
    /// <b>Prerequisites:</b> Hosts must register logging and <see cref="IAccountKeyNormalizer"/> (typically via
    /// <c>AddNntpSessionRedis</c> or session core) before resolving <see cref="MySqlNntpCredentialValidator"/>.
    /// </para>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers MySQL-backed NNTP authentication using the <c>MainDB</c> connection string from host configuration.
        /// </summary>
        /// <param name="services">
        /// Host service collection. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="configuration">
        /// Root <see cref="IConfiguration"/> (for example <c>builder.Configuration</c>). Must not be <see langword="null"/>.
        /// </param>
        /// <returns><paramref name="services"/> for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <c>ConnectionStrings:MainDB</c> is missing or whitespace-only.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Reads <c>configuration.GetConnectionString("MainDB")</c> and delegates to <see cref="AddNntpMySqlAuth"/>. This is
        /// the production entry point for NNRPD and NNTPD when <c>appsettings</c> supplies <c>ConnectionStrings:MainDB</c>
        /// (see <c>Docs/nntp-authentication.md</c>).
        /// </para>
        /// <para>
        /// <b>Fatal startup:</b> Missing <c>MainDB</c> throws during service registration so the host never starts with an
        /// undefined auth database. Malformed or placeholder connection strings throw <see cref="ArgumentException"/> from
        /// <see cref="MySqlAuthConnectionStringValidator"/> inside <see cref="MySqlAuthOptions"/> construction.
        /// </para>
        /// </remarks>
        public static IServiceCollection AddNntpMySqlAuthFromHostConfiguration(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            string? connectionString = configuration.GetConnectionString("MainDB");

            return string.IsNullOrWhiteSpace(connectionString)
                ? throw new InvalidOperationException("Connection string 'MainDB' is required.")
                : services.AddNntpMySqlAuth(connectionString);
        }

        /// <summary>
        /// Registers MySQL-backed NNTP authentication services for a validated connection string.
        /// </summary>
        /// <param name="services">
        /// Host service collection. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="connectionString">
        /// MySQL connection string for the <c>nntpusers</c> table. Validated by
        /// <see cref="MySqlAuthConnectionStringValidator"/> when <see cref="MySqlAuthOptions"/> is constructed (non-empty
        /// server and database, parseable builder, no placeholder credentials).
        /// </param>
        /// <returns><paramref name="services"/> for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="services"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="connectionString"/> is blank, malformed, or contains placeholder credentials.
        /// </exception>
        /// <remarks>
        /// <para><b>Replaced registrations:</b> Removes any prior implementations of:</para>
        /// <list type="bullet">
        /// <item><description><see cref="INntpCredentialValidator"/></description></item>
        /// <item><description><see cref="INntpSaslAccountAuthenticator"/></description></item>
        /// <item><description><see cref="ICramMd5CredentialStore"/> and <see cref="IScramCredentialStore"/></description></item>
        /// <item><description><see cref="INntpUserRecordStore"/>, <see cref="MySqlUserRecordStore"/>, <see cref="MySqlUserRecordCache"/>, <see cref="AuthMySqlMetrics"/></description></item>
        /// </list>
        /// <para><b>Singleton graph (production):</b></para>
        /// <list type="number">
        /// <item><description><see cref="MySqlAuthOptions"/> — validated connection string and default auth-cache TTL.</description></item>
        /// <item><description><see cref="AuthMySqlMetrics"/> — OpenTelemetry counters and histograms for auth MySQL paths.</description></item>
        /// <item><description><see cref="MySqlUserRecordCache"/> — burst deduplication cache (TTL from options).</description></item>
        /// <item><description><see cref="MySqlUserRecordStore"/> — inner MySQL <c>nntpusers</c> lookups.</description></item>
        /// <item><description><see cref="CachingMySqlUserRecordStore"/> as <see cref="INntpUserRecordStore"/> — read-through decorator.</description></item>
        /// <item>
        /// <description>
        /// <see cref="MySqlNntpCredentialValidator"/> — concrete singleton; also exposed as <see cref="INntpCredentialValidator"/>
        /// and <see cref="INntpSaslAccountAuthenticator"/> (same instance for both interfaces).
        /// </description>
        /// </item>
        /// <item><description><see cref="MySqlCramMd5CredentialStore"/> as <see cref="ICramMd5CredentialStore"/>.</description></item>
        /// <item><description><see cref="MySqlScramCredentialStore"/> as <see cref="IScramCredentialStore"/>.</description></item>
        /// <item><description><see cref="MySqlAuthConnectivityValidator"/> as <see cref="Microsoft.Extensions.Hosting.IHostedService"/> — fail-fast <c>SELECT 1</c> at host start.</description></item>
        /// </list>
        /// <para>
        /// <b>Tests and tools:</b> Integration tests and harnesses may call this overload directly with an in-memory connection
        /// string instead of binding <c>MainDB</c> from configuration.
        /// </para>
        /// </remarks>
        public static IServiceCollection AddNntpMySqlAuth(this IServiceCollection services, string connectionString)
        {
            ArgumentNullException.ThrowIfNull(services);

            _ = services.RemoveAll<INntpCredentialValidator>();
            _ = services.RemoveAll<INntpSaslAccountAuthenticator>();
            _ = services.RemoveAll<ICramMd5CredentialStore>();
            _ = services.RemoveAll<IScramCredentialStore>();
            _ = services.RemoveAll<INntpUserRecordStore>();
            _ = services.RemoveAll<MySqlUserRecordStore>();
            _ = services.RemoveAll<MySqlUserRecordCache>();
            _ = services.RemoveAll<AuthMySqlMetrics>();

            _ = services.AddSingleton(new MySqlAuthOptions(connectionString));
            _ = services.AddSingleton(static _ => new AuthMySqlMetrics());
            _ = services.AddSingleton(static sp =>
            {
                MySqlAuthOptions options = sp.GetRequiredService<MySqlAuthOptions>();
                return new MySqlUserRecordCache(options.AuthCacheTtl);
            });
            _ = services.AddSingleton(static sp => new MySqlUserRecordStore(
                sp.GetRequiredService<MySqlAuthOptions>(),
                sp.GetRequiredService<ILogger<MySqlUserRecordStore>>(),
                sp.GetRequiredService<AuthMySqlMetrics>()));
            _ = services.AddSingleton<INntpUserRecordStore>(static sp => new CachingMySqlUserRecordStore(
                sp.GetRequiredService<MySqlUserRecordStore>(),
                sp.GetRequiredService<MySqlUserRecordCache>(),
                sp.GetRequiredService<AuthMySqlMetrics>(),
                sp.GetRequiredService<ILogger<CachingMySqlUserRecordStore>>()));
            _ = services.AddSingleton(static sp => new MySqlNntpCredentialValidator(
                sp.GetRequiredService<INntpUserRecordStore>(),
                sp.GetRequiredService<IAccountKeyNormalizer>(),
                sp.GetRequiredService<MySqlUserRecordCache>(),
                sp.GetRequiredService<AuthMySqlMetrics>(),
                sp.GetRequiredService<ILogger<MySqlNntpCredentialValidator>>()));
            _ = services.AddSingleton<INntpCredentialValidator>(static sp => sp.GetRequiredService<MySqlNntpCredentialValidator>());
            _ = services.AddSingleton<INntpSaslAccountAuthenticator>(static sp => sp.GetRequiredService<MySqlNntpCredentialValidator>());
            _ = services.AddSingleton<ICramMd5CredentialStore>(static sp => new MySqlCramMd5CredentialStore(
                sp.GetRequiredService<INntpUserRecordStore>(),
                sp.GetRequiredService<ILogger<MySqlCramMd5CredentialStore>>()));
            _ = services.AddSingleton<IScramCredentialStore>(static sp => new MySqlScramCredentialStore(
                sp.GetRequiredService<INntpUserRecordStore>(),
                sp.GetRequiredService<ILogger<MySqlScramCredentialStore>>()));
            _ = services.AddHostedService(static sp => new MySqlAuthConnectivityValidator(
                sp.GetRequiredService<MySqlAuthOptions>(),
                sp.GetRequiredService<ILogger<MySqlAuthConnectivityValidator>>()));

            return services;
        }
    }
}
