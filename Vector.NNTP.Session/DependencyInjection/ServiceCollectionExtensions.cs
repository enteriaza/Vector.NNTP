// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ServiceCollectionExtensions.cs -- DI registration for core session services.
//
// Registers node-local session registry and in-memory coordinators used by tests and as defaults before Redis replaces them.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Session.DependencyInjection
{
    /// <summary>
    /// Registers core session services (node-local registry and in-memory coordinators for tests).
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="ISessionDatabase"/> and in-memory coordinator defaults.
        /// </summary>
        /// <remarks>
        /// Production hosts call <c>AddNntpSessionRedis</c> first, which replaces coordinators with Redis implementations.
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <returns><paramref name="services"/> for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
        public static IServiceCollection AddNntpSessionCore(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            _ = services.AddOptions<NntpRateAllocationOptions>();
            _ = services.AddOptions<NntpSessionIdleOptions>()
                .BindConfiguration(NntpSessionIdleOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.TryAddSingleton<ISessionDatabase, InMemorySessionDatabase>();
            services.TryAddSingleton<INntpSessionCoordinator, InMemorySessionCoordinator>();
            services.TryAddSingleton<INntpSessionCountCoordinator, InMemorySessionCountCoordinator>();
            services.TryAddSingleton<INntpBlockQuotaCoordinator, InMemoryBlockQuotaCoordinator>();
            services.TryAddSingleton<INntpRateAllocationCoordinator, NodeLocalRateAllocationCoordinator>();
            services.TryAddSingleton<INntpTransitPeerCoordinator, InMemoryTransitPeerCoordinator>();
            services.TryAddSingleton<INodeSessionRegistry, InMemoryNodeSessionRegistry>();
            services.TryAddSingleton<IAccountKeyNormalizer, Blake3AccountKeyNormalizer>();
            services.TryAddSingleton<NntpQuotaEnforcer>();
            return services;
        }
    }
}
