// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.HostedServices;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;

namespace Vector.NNTP.HistoryDB.DependencyInjection
{
    /// <summary>
    /// Dependency-injection registration for transit history deduplication.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers HistoryDB (Rocks, Redis, memory) for NNTPD transit CHECK.
        /// </summary>
        /// <remarks>
        /// Call after <c>AddNntpSessionRedis</c> and before <c>AddNntpSocketsTransit</c> so rebuild completes before listeners accept CHECK.
        /// </remarks>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Host configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddNntpHistoryDatabase(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            _ = services.AddOptions<HistoryDbOptions>()
                .Bind(configuration.GetSection(HistoryDbOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<HistoryDbOptions>, HistoryDbOptionsValidator>());

            _ = services.AddSingleton<HistoryMetrics>();
            _ = services.AddSingleton<HistoryMemoryCache>(sp =>
            {
                HistoryDbOptions opts = sp.GetRequiredService<IOptions<HistoryDbOptions>>().Value;
                return new HistoryMemoryCache(opts.MemoryLimitBytes, sp.GetRequiredService<HistoryMetrics>());
            });
            _ = services.AddSingleton<RocksHistoryStore>();
            _ = services.AddSingleton<HistoryRedisStore>();
            _ = services.AddSingleton<HistoryGenerationStore>();
            _ = services.AddSingleton<HistoryRocksPersistPump>();
            _ = services.AddSingleton<HistoryDatabaseService>();
            _ = services.AddSingleton<IHistoryDatabase>(sp => sp.GetRequiredService<HistoryDatabaseService>());

            _ = services.AddHostedService<HistoryDatabaseHostedService>();
            _ = services.AddHostedService<HistoryBackgroundWorkerHostedService>();
            _ = services.AddHostedService<HistoryRocksStatsLogHostedService>();

            return services;
        }
    }
}
