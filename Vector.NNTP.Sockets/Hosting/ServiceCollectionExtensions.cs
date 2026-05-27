// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: DI registration for reader and transit socket hosts.

namespace Vector.NNTP.Sockets.Hosting
{
    using Authentication;
    using Configuration;
    using HostProfile;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;
    using Transport;
    using Tls;

    /// <summary>
    /// Registers Vector.NNTP.Sockets services for NNRPD (reader) or NNTPD (transit) hosts.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers NNTP socket server services for a reader (NNRPD) host.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddNntpSocketsReader(this IServiceCollection services)
        {
            return services.AddNntpSocketsCore()
                .AddSingleton<INntpHostProfile, NntpReaderHostProfile>()
                .AddNntpSocketsDevelopmentStubs();
        }

        /// <summary>
        /// Registers NNTP socket server services for a transit (NNTPD) host.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddNntpSocketsTransit(this IServiceCollection services)
        {
            return services.AddNntpSocketsCore()
                .AddSingleton<INntpHostProfile, NntpTransitHostProfile>()
                .AddNntpSocketsDevelopmentStubs();
        }

        private static IServiceCollection AddNntpSocketsCore(this IServiceCollection services)
        {
            services.AddOptions<NntpServerOptions>()
                .BindConfiguration(NntpServerOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NntpServerOptions>, NntpServerOptionsValidator>());
            services.AddSingleton<NntpAuthenticationService>();
            services.AddSingleton<NntpInFlightSessionTracker>();
            services.AddSingleton<NntpCommandDispatcher>();
            services.AddSingleton<NntpSessionRunner>();
            services.AddSingleton<NntpSocketAcceptor>();
            services.AddHostedService<NntpSocketHostedService>();
            services.AddNntpSocketsEncryptionTls();
            return services;
        }

        /// <summary>
        /// Registers TLS certificate bridging from Vector.NNTP.Encryption.
        /// </summary>
        /// <remarks>
        /// <para>Requires <see cref="Vector.NNTP.Encryption.ServiceCollectionExtensions.AddEncryption"/> on the host first.</para>
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddNntpSocketsEncryptionTls(this IServiceCollection services)
        {
            services.TryAddSingleton<ITlsCertificateSource, EncryptionTlsCertificateSource>();
            return services;
        }
    }
}
