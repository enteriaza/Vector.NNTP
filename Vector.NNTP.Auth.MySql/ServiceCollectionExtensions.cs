// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: DI registration helpers for MySQL-based NNTP authentication.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        /// Resolves the database connection in order: <c>NntpUsers:ConnectionString</c>, then
        /// <c>ConnectionStrings:MainDB</c>. When neither is set, this method is a no-op and development credential stubs remain active.
        /// </para>
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Root host configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddNntpMySqlAuthFromHostConfiguration(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            IConfigurationSection nntpUsersSection = configuration.GetSection(NntpUsersOptions.SectionName);
            string? connectionString = nntpUsersSection["ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = configuration.GetConnectionString("MainDB");
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return services;
            }

            if (!string.IsNullOrWhiteSpace(nntpUsersSection["ConnectionString"]))
            {
                return services.AddNntpMySqlAuth(nntpUsersSection);
            }

            return services.AddNntpMySqlAuth(nntpUsersSection, connectionString);
        }

        /// <summary>
        /// Registers MySQL-backed <see cref="INntpCredentialValidator"/> and <see cref="ICramMd5CredentialStore"/> services.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Configuration:</b> This method binds <see cref="NntpUsersOptions"/> from the supplied configuration section
        /// and enables data-annotations validation with <c>ValidateOnStart</c>. Misconfiguration will fail host startup.
        /// </para>
        /// <para>
        /// <b>Integration:</b> Hosts should call this after <c>AddNntpSocketsReader</c> or <c>AddNntpSocketsTransit</c>.
        /// The MySQL services will override the development <see cref="INntpCredentialValidator"/> stub registered by the
        /// sockets assembly while leaving other stubs (such as storage) in place.
        /// </para>
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <param name="configurationSection">Configuration section containing <see cref="NntpUsersOptions"/> values.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddNntpMySqlAuth(this IServiceCollection services, IConfiguration configurationSection) =>
            AddNntpMySqlAuth(services, configurationSection, configurationSection["ConnectionString"] ?? string.Empty);

        /// <summary>
        /// Registers MySQL-backed authentication using <paramref name="configurationSection"/> for options and an explicit connection string.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configurationSection">Configuration section for <see cref="NntpUsersOptions"/> (for example command timeout).</param>
        /// <param name="connectionString">MySQL connection string for the <c>nntpusers</c> table.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is null or whitespace.</exception>
        public static IServiceCollection AddNntpMySqlAuth(
            this IServiceCollection services,
            IConfiguration configurationSection,
            string connectionString)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configurationSection is null)
            {
                throw new ArgumentNullException(nameof(configurationSection));
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is required.", nameof(connectionString));
            }

            services.RemoveAll<INntpCredentialValidator>();
            services.RemoveAll<ICramMd5CredentialStore>();
            services.RemoveAll<INntpSessionAdmissionTracker>();

            services.AddOptions<NntpUsersOptions>()
                .Bind(configurationSection)
                .Configure(options => options.ConnectionString = connectionString)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddSingleton<INntpSessionAdmissionTracker, NntpSessionAdmissionTracker>();
            services.TryAddSingleton<INntpUserRecordStore, MySqlUserRecordStore>();
            services.AddSingleton<INntpCredentialValidator, MySqlNntpCredentialValidator>();
            services.AddSingleton<ICramMd5CredentialStore, MySqlCramMd5CredentialStore>();

            return services;
        }
    }
}
