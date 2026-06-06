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
    /// Extension methods for wiring MySQL-backed NNTP authentication into a host.
    /// </summary>
    /// <remarks>
    /// <para><b>Host contract:</b> Call after <c>AddNntpSocketsReader</c> or <c>AddNntpSocketsTransit</c> so MySQL
    /// credential services replace the development authentication stubs from the sockets assembly.</para>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers MySQL-backed NNTP authentication when a connection string is available on the host configuration.
        /// </summary>
        /// <remarks>
        /// <para>Uses the connection string named <c>MainDB</c>.</para>
        /// <para><b>Fatal startup behavior:</b> Missing or blank <c>MainDB</c> throws and host startup fails.</para>
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Root host configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when <c>ConnectionStrings:MainDB</c> is missing or blank.</exception>
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
        /// Registers MySQL-backed <see cref="INntpCredentialValidator"/> and SASL credential store services.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="connectionString">MySQL connection string for the <c>nntpusers</c> table.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is invalid.</exception>
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
