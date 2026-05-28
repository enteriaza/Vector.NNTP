// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: DI registration for reader and transit socket hosts.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Tls;
using Vector.NNTP.Sockets.Transport;

namespace Vector.NNTP.Sockets.Hosting
{
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
            _ = services.AddOptions<NntpServerOptions>()
                .BindConfiguration(NntpServerOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NntpServerOptions>, NntpServerOptionsValidator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<NntpServerOptions>, NntpServerIdleTimeoutPostConfigure>());
            _ = services.AddNntpSessionCore();
            _ = services.AddSingleton<NntpAuthenticationService>();
            _ = services.AddSingleton<NntpInFlightSessionTracker>();
            _ = services.AddSingleton<NntpCommandDispatcher>();
            _ = services.AddSingleton<NntpSessionRunner>();
            _ = services.AddSingleton<NntpSocketAcceptor>();
            _ = services.AddHostedService<NntpSocketHostedService>();
            _ = services.AddNntpSocketsEncryptionTls();
            return services;
        }

        /// <summary>
        /// Registers TLS certificate bridging from Vector.NNTP.Encryption.
        /// </summary>
        /// <remarks>
        /// <para>Requires <see cref="Encryption.DependencyInjection.ServiceCollectionExtensions.AddEncryption"/> on the host first.</para>
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
