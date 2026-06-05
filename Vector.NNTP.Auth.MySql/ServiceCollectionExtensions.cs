// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Session.Accounts;
using Vector.NNTP.Sockets.Authentication;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Extension methods for wiring MySQL-backed NNTP authentication into a host.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers MySQL-backed NNTP authentication when a connection string is available on the host configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Uses the connection string named <c>MainDB</c>.
        /// </para>
        /// <para>
        /// <b>Fatal startup behavior:</b> MySQL authentication depends on the main database connection. If <c>MainDB</c>
        /// is missing or blank, this method throws and host startup fails.
        /// </para>
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Root host configuration.</param>
        /// <returns>The service collection for chaining.</returns>
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
        /// Registers MySQL-backed <see cref="INntpCredentialValidator"/> and <see cref="ICramMd5CredentialStore"/> services.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Configuration:</b> The MySQL command timeouts and pooling policy are supplied through the connection string
        /// (for example <c>DefaultCommandTimeout</c>). There is no additional options section bound by this method.
        /// </para>
        /// <para>
        /// <b>Registration:</b> <see cref="MySqlAuthOptions"/> is registered as a singleton with the validated connection
        /// string, and <see cref="INntpUserRecordStore"/> is registered as
        /// <c>AddSingleton&lt;INntpUserRecordStore, MySqlUserRecordStore&gt;</c> so the container constructs the store by
        /// type rather than a factory delegate.
        /// </para>
        /// <para>
        /// <b>Integration:</b> Hosts should call this after <c>AddNntpSocketsReader</c> or <c>AddNntpSocketsTransit</c>.
        /// The MySQL services will override the development <see cref="INntpCredentialValidator"/> stub registered by the
        /// sockets assembly while leaving other stubs (such as storage) in place.
        /// </para>
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <param name="connectionString">MySQL connection string for the <c>nntpusers</c> table.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is null or whitespace.</exception>
        public static IServiceCollection AddNntpMySqlAuth(this IServiceCollection services, string connectionString)
        {
            ArgumentNullException.ThrowIfNull(services);

            _ = services.RemoveAll<INntpCredentialValidator>();
            _ = services.RemoveAll<INntpSaslAccountAuthenticator>();
            _ = services.RemoveAll<ICramMd5CredentialStore>();
            _ = services.RemoveAll<IScramCredentialStore>();

            _ = services.AddSingleton(new MySqlAuthOptions(connectionString));
            _ = services.AddSingleton<INntpUserRecordStore, MySqlUserRecordStore>();
            _ = services.AddSingleton(static (IServiceProvider sp) => new MySqlNntpCredentialValidator(
                sp.GetRequiredService<INntpUserRecordStore>(),
                sp.GetRequiredService<IAccountKeyNormalizer>(),
                sp.GetRequiredService<ILogger<MySqlNntpCredentialValidator>>()));
            _ = services.AddSingleton<INntpCredentialValidator>(static sp => sp.GetRequiredService<MySqlNntpCredentialValidator>());
            _ = services.AddSingleton<INntpSaslAccountAuthenticator>(static sp => sp.GetRequiredService<MySqlNntpCredentialValidator>());
            _ = services.AddSingleton<ICramMd5CredentialStore>(static sp => new MySqlCramMd5CredentialStore(
                sp.GetRequiredService<INntpUserRecordStore>(),
                sp.GetRequiredService<ILogger<MySqlCramMd5CredentialStore>>()));
            _ = services.AddSingleton<IScramCredentialStore>(static sp => new MySqlScramCredentialStore(
                sp.GetRequiredService<INntpUserRecordStore>(),
                sp.GetRequiredService<ILogger<MySqlScramCredentialStore>>()));

            return services;
        }
    }
}
