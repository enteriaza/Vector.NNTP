// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ServiceCollectionExtensions.cs -- DI entry points for the Encryption / certificate subsystem.
//
// Hosts (NNRPD, NNTPD) load their JSON configuration and bind option snapshots before calling AddEncryption.
// The library never reads JSON files, environment variables, or IConfiguration directly.

using Microsoft.Extensions.DependencyInjection;
using Vector.NNTP.Encryption.Certificates;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Dns;
using Vector.NNTP.Encryption.Telemetry;

namespace Vector.NNTP.Encryption.DependencyInjection
{
    /// <summary>
    /// Dependency-injection registration for automatic TLS certificate provisioning and renewal.
    /// </summary>
    /// <remarks>
    /// <para><b>Host contract:</b> Callers must register <see cref="LetsEncryptOptions"/> and
    /// <see cref="NntpServerOptions"/> via <c>AddOptions</c>, configuration binding, and <c>ValidateOnStart</c>
    /// before <see cref="AddEncryption(IServiceCollection)"/>. The library does not read host JSON files.</para>
    ///
    /// <para><b>Hosted service:</b> Registers <see cref="CertificateRenewalService"/> as a
    /// <see cref="IHostedService"/> when Let's Encrypt is enabled at runtime.</para>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers certificate renewal and ACME provider services.
        /// </summary>
        /// <param name="services">Application service collection.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <remarks>
        /// <para><b>Prerequisite:</b> <see cref="IOptions{LetsEncryptOptions}"/> and
        /// <see cref="IOptions{NntpServerOptions}"/> must already be registered and validated on start.</para>
        /// </remarks>
        public static IServiceCollection AddEncryption(this IServiceCollection services)
        {
            _ = services.AddSingleton<IValidateOptions<LetsEncryptOptions>, LetsEncryptOptionsValidator>();
            _ = services.AddSingleton(static _ => new EncryptionMetrics());
            _ = services.AddSingleton<IDnsTxtPropagationProbe>(static sp => new AuthoritativeDnsTxtPropagationProbe(
                sp.GetRequiredService<ILogger<AuthoritativeDnsTxtPropagationProbe>>(),
                sp.GetRequiredService<EncryptionMetrics>()));
            _ = services.AddSingleton<IPostConfigureOptions<LetsEncryptOptions>, LetsEncryptOptionsPostConfigurator>();
            _ = services.AddSingleton<CertificateRenewalService>();
            _ = services.AddSingleton<ICertificateRenewalPublisher>(static sp => sp.GetRequiredService<CertificateRenewalService>());
            _ = services.AddHostedService(static provider => provider.GetRequiredService<CertificateRenewalService>());
            return services;
        }

        /// <summary>
        /// Registers encryption services with explicit option snapshots (unit tests and harness hosts).
        /// </summary>
        /// <param name="services">Application service collection.</param>
        /// <param name="letsEncryptOptions">Let's Encrypt configuration snapshot.</param>
        /// <param name="nntpServerOptions">NNTP server identity snapshot.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either options instance is null.</exception>
        public static IServiceCollection AddEncryption(
            this IServiceCollection services,
            LetsEncryptOptions letsEncryptOptions,
            NntpServerOptions nntpServerOptions)
        {
            ArgumentNullException.ThrowIfNull(letsEncryptOptions);
            ArgumentNullException.ThrowIfNull(nntpServerOptions);
            _ = services.AddSingleton<IOptions<LetsEncryptOptions>>(new OptionsWrapper<LetsEncryptOptions>(letsEncryptOptions));
            _ = services.AddSingleton<IOptions<NntpServerOptions>>(new OptionsWrapper<NntpServerOptions>(nntpServerOptions));
            return services.AddEncryption();
        }
    }
}
