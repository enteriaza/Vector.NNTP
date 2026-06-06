// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ServiceCollectionExtensions.cs -- DI registration entry points for MessageBus pool, publisher, and consumer services.
//
// Hosts (NNRPD, NNTPD) bind RabbitMQOptions from JSON and call AddMessageBus. The library never reads configuration files.
// ValidateOnStart on RabbitMQOptions must run before AddMessageBus so pool sizing and endpoints fail fast at startup.
//
// Usage:
//   services.AddOptions<RabbitMQOptions>().BindConfiguration("RabbitMQ").ValidateOnStart();
//   services.AddMessageBus();

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Consuming;
using Vector.NNTP.MessageBus.Health;
using Vector.NNTP.MessageBus.Metrics;
using Vector.NNTP.MessageBus.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Vector.NNTP.MessageBus.DependencyInjection
{
    /// <summary>
    /// Dependency-injection registration for MessageBus pool, publisher, and consumer components.
    /// </summary>
    /// <remarks>
    /// <para><b>Host contract:</b> Callers must register <see cref="RabbitMQOptions"/> via <c>AddOptions</c>,
    /// <c>BindConfiguration</c>, and <c>ValidateOnStart</c> before <see cref="AddMessageBus(IServiceCollection)"/>.
    /// MessageBus does not read JSON or environment variables directly.</para>
    ///
    /// <para><b>Hosted services:</b> Registers <see cref="RabbitMqPoolSupervisor"/> (pool start/stop),
    /// <see cref="RabbitMqBackgroundScaler"/> (scale-up), and <see cref="RabbitMqPoolFlowControlMonitor"/> (blocked
    /// quarantine).</para>
    ///
    /// <para><b>Singleton lifetime:</b> <see cref="ConnectionPool"/>, factories, health, publisher pool, and consumer manager
    /// are process-singletons aligned with long-lived TCP connections.</para>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers MessageBus pool, publisher, consumer, and hosted-service components.
        /// </summary>
        /// <param name="services">Application service collection.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <remarks>
        /// <para><b>Prerequisite:</b> <see cref="IOptions{RabbitMQOptions}"/> must already be registered and validated on
        /// start; otherwise pool services receive empty options.</para>
        /// </remarks>

        public static IServiceCollection AddMessageBus(this IServiceCollection services)
        {
            services.TryAddSingleton<IValidateOptions<RabbitMQOptions>, RabbitMQOptionsValidator>();
            services.TryAddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
            services.TryAddSingleton<HostHealthTracker>();
            services.TryAddSingleton(static _ => new MessageBusMetrics());
            services.TryAddSingleton<ConnectionPool>();
            services.TryAddSingleton<IRabbitMqPoolHealth, RabbitMqPoolHealth>();
            services.TryAddSingleton<IRabbitMqPublisherPool, RabbitMqPublisherPool>();
            services.TryAddSingleton<IRabbitMqConsumerManager, RabbitMqConsumerManager>();
            _ = services.AddHostedService<RabbitMqBackgroundScaler>();
            _ = services.AddHostedService<RabbitMqPoolFlowControlMonitor>();
            _ = services.AddHostedService<RabbitMqPoolSupervisor>();
            return services;
        }

        /// <summary>
        /// Registers MessageBus with an explicit <see cref="RabbitMQOptions"/> snapshot (unit tests and harness hosts).
        /// </summary>
        /// <param name="services">Application service collection.</param>
        /// <param name="options">Pre-built options instance wrapped in <see cref="OptionsWrapper{RabbitMQOptions}"/>.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <remarks>
        /// <para><b>Validation:</b> Does not run <see cref="RabbitMQOptionsValidator"/> unless the host also registers
        /// <c>IValidateOptions&lt;RabbitMQOptions&gt;</c> and <c>ValidateOnStart</c>. Tests typically supply valid snapshots
        /// directly.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public static IServiceCollection AddMessageBus(this IServiceCollection services, RabbitMQOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _ = services.AddSingleton<IOptions<RabbitMQOptions>>(new OptionsWrapper<RabbitMQOptions>(options));
            return services.AddMessageBus();
        }
    }
}

