// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ServiceCollectionExtensions.cs -- DI registration for Redis-backed session coordination.
//
// Hosts bind NntpSessionCoordinationOptions from the Redis JSON section and call AddNntpSessionRedis.
// The library never reads host configuration files directly.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vector.NNTP.Session.DependencyInjection;
using Vector.NNTP.Session.Redis.Connections;
using Vector.NNTP.Session.Redis.Health;
using Vector.NNTP.Session.Redis.HostedServices;

namespace Vector.NNTP.Session.Redis.DependencyInjection
{
    /// <summary>
    /// Dependency-injection registration for Redis-backed session coordination.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers Redis session admission, quota, rate allocation, heartbeat, and reconciliation.
        /// </summary>
        /// <remarks>
        /// Binds the top-level <c>Redis</c> configuration section with <see cref="NntpSessionCoordinationOptions.Hosts"/>
        /// and starts a multiplexer pool with at least one live connection at host startup.
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Host configuration root.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddNntpSessionRedis(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            _ = services.AddNntpSessionCore();
            _ = services.AddSingleton<IValidateOptions<NntpSessionCoordinationOptions>, NntpSessionCoordinationOptionsValidator>();
            _ = services.AddOptions<NntpSessionCoordinationOptions>()
                .Bind(configuration.GetSection(NntpSessionCoordinationOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            _ = services.AddSingleton<RedisMultiplexerFactory>();
            _ = services.AddSingleton<RedisHostHealthTracker>();
            _ = services.AddSingleton<RedisMultiplexerPool>();
            _ = services.AddSingleton<IRedisPoolHealth, RedisPoolHealth>();
            _ = services.AddSingleton<IRedisConnectionAccessor, RedisConnectionAccessor>();
            _ = services.AddHostedService<RedisMultiplexerPoolSupervisor>();
            _ = services.AddHostedService<RedisMultiplexerBackgroundScaler>();

            // Node purge runs before heartbeat/reconciliation; register before those hosted services.
            _ = services.AddOptions<NntpNodeIdentityOptions>()
                .Bind(configuration.GetSection(NntpNodeIdentityOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            _ = services.AddHostedService<NodeSessionLifecycleHostedService>();

            _ = services.RemoveAll<INodeSessionRegistry>();
            _ = services.RemoveAll<INntpSessionCoordinator>();
            _ = services.RemoveAll<INntpBlockQuotaCoordinator>();
            _ = services.RemoveAll<INntpSessionCountCoordinator>();
            _ = services.RemoveAll<INntpRateAllocationCoordinator>();
            _ = services.RemoveAll<INntpTransitPeerCoordinator>();

            _ = services.AddSingleton<IRedisSessionReconciliationCoordinator, RedisSessionReconciliationCoordinator>();
            _ = services.AddSingleton<INntpSessionCoordinator, RedisSessionCoordinator>();
            _ = services.AddSingleton<INntpBlockQuotaCoordinator, RedisBlockQuotaCoordinator>();
            _ = services.AddSingleton<INntpSessionCountCoordinator, RedisSessionCountCoordinator>();
            _ = services.AddSingleton<INntpRateAllocationCoordinator, RedisRateAllocationCoordinator>();
            _ = services.AddSingleton<INntpTransitPeerCoordinator, RedisTransitPeerCoordinator>();
            _ = services.AddSingleton<INodeSessionRegistry, RedisNodeSessionRegistry>();
            _ = services.AddSingleton<IRedisSessionLeaseRefresher, RedisSessionLeaseRefresher>();
            _ = services.AddHostedService<RedisSessionHeartbeatHostedService>();

            NntpSessionCoordinationOptions bootstrap = new();
            configuration.GetSection(NntpSessionCoordinationOptions.SectionName).Bind(bootstrap);
            int reconciliationInterval = bootstrap.ReconciliationIntervalSeconds;
            if (reconciliationInterval > 0)
            {
                _ = services.AddHostedService<RedisSessionReconciliationHostedService>();
            }

            return services;
        }
    }
}
