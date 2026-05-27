// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

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
        /// Uses the host connection string named <c>ConnectionStrings:MainDB</c>. If that connection string is not set,
        /// this method is a no-op and development credential stubs remain active.
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

            IConfigurationSection nntpUsersSection = configuration.GetSection(NntpUsersOptions.SectionName);
            string? connectionString = configuration.GetConnectionString("MainDB");

            return string.IsNullOrWhiteSpace(connectionString) ? services : services.AddNntpMySqlAuth(nntpUsersSection, connectionString);
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
        public static IServiceCollection AddNntpMySqlAuth(this IServiceCollection services, IConfiguration configurationSection)
        {
            return AddNntpMySqlAuth(services, configurationSection, configurationSection["ConnectionString"] ?? string.Empty);
        }

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
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configurationSection);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is required.", nameof(connectionString));
            }

            _ = services.RemoveAll<INntpCredentialValidator>();
            _ = services.RemoveAll<ICramMd5CredentialStore>();
            _ = services.RemoveAll<INntpSessionAdmissionTracker>();

            _ = services.AddOptions<NntpUsersOptions>()
                .Bind(configurationSection)
                .Configure(options => options.ConnectionString = connectionString)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            _ = services.AddSingleton<INntpSessionAdmissionTracker, NntpSessionAdmissionTracker>();
            _ = services.AddSingleton<INntpUserRecordStore, MySqlUserRecordStore>();
            _ = services.AddSingleton<INntpCredentialValidator, MySqlNntpCredentialValidator>();
            _ = services.AddSingleton<ICramMd5CredentialStore, MySqlCramMd5CredentialStore>();

            return services;
        }
    }
}
