// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: SpamAssassin client dependency-injection registration.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vector.NNTP.Filters.SpamAssassin;
using SpamAssassinClient = Vector.NNTP.Filters.SpamAssassin.SpamAssassin;

namespace Vector.NNTP.Filters.DependencyInjection
{
    /// <summary>
    /// Dependency-injection registration for SpamAssassin filter clients.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="ISpamAssassin"/> and binds <see cref="SpamAssassinOptions"/> from configuration.
        /// </summary>
        /// <param name="services">Service collection to extend.</param>
        /// <param name="configuration">Host configuration root.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Registers <see cref="ISpamAssassin"/> and concrete <see cref="SpamAssassin"/> as singletons. Options bind from
        /// <see cref="SpamAssassinOptions.SectionName"/> with data annotations and <see cref="SpamAssassinOptionsValidator"/>
        /// normalization, failing fast at startup via <c>ValidateOnStart</c>.
        /// </para>
        /// <para>
        /// Each scan still opens a dedicated TCP connection; the singleton holds configuration and round-robin state only.
        /// </para>
        /// </remarks>
        public static IServiceCollection AddSpamAssassin(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            _ = services
                .AddOptions<SpamAssassinOptions>()
                .Bind(configuration.GetSection(SpamAssassinOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            _ = services.AddSingleton<IValidateOptions<SpamAssassinOptions>, SpamAssassinOptionsValidator>();
            _ = services.AddSingleton<ISpamAssassin, SpamAssassinClient>();
            _ = services.AddSingleton<SpamAssassinClient>();

            return services;
        }
    }
}
