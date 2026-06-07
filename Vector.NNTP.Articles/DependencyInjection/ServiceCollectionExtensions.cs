// <copyright file="ServiceCollectionExtensions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: transit spool pipeline dependency-injection registration.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Diagnostics;
using Vector.NNTP.Articles.Logging;
using Vector.NNTP.Filters.DependencyInjection;
using Vector.NNTP.Filters.PostFilter;
using Vector.NNTP.Filters.SpamAssassin;
using Vector.NNTP.Articles.Hosting;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Articles.DependencyInjection
{
    /// <summary>
    /// Dependency-injection registration for the transit article spool queue, disk writer workers, and related metrics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hosts call <see cref="AddNntpArticlesTransitSpool"/> after socket and history services are registered so
    /// <see cref="IOptions{NntpServerOptions}"/> and <see cref="HistoryDB.Abstractions.IHistoryDatabase"/> resolve for
    /// spool components. The NNTPD transit host wires this immediately after <c>AddNntpSocketsTransit</c> in
    /// <c>Program.Nntp.cs</c>.
    /// </para>
    /// <para>
    /// This extension does not bind <see cref="NntpServerOptions"/> itself. Spool settings such as
    /// <see cref="NntpServerOptions.SpoolDir"/>, <see cref="NntpServerOptions.SpoolQueueCapacity"/>, and
    /// <see cref="NntpServerOptions.MaxQueuedBytes"/> must come from the host's existing options registration.
    /// </para>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the transit spool pipeline, production <see cref="INntpTransitStorage"/>, and hosted writer workers.
        /// </summary>
        /// <param name="services">Service collection to extend.</param>
        /// <param name="configuration">Host configuration for <see cref="PostFilterOptions"/> and <see cref="SpamAssassinOptions"/>; pass <see langword="null"/> in tests.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="services"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para><b>Prerequisites:</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// <see cref="IOptions{NntpServerOptions}"/> (or <see cref="IOptionsMonitor{NntpServerOptions}"/>) bound by the
        /// host — for example via <c>AddNntpSocketsTransit</c>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <see cref="HistoryDB.Abstractions.IHistoryDatabase"/> from <c>AddNntpHistoryDatabase</c> — required by
        /// <see cref="NntpSpoolWriterPump"/> for reservation release on preprocess/write failure.
        /// </description>
        /// </item>
        /// <item><description>Host logging (<see cref="ILogger{T}"/> categories resolve automatically).</description></item>
        /// </list>
        /// <para><b>Singleton registrations:</b></para>
        /// <list type="bullet">
        /// <item><description><see cref="NntpSpoolMetrics"/> — OpenTelemetry counters and gauges for queue and writers.</description></item>
        /// <item><description><see cref="NntpSpoolWriteQueue"/> — bounded in-memory transit queue.</description></item>
        /// <item><description><see cref="INntpNewsLog"/> — INN-style <c>pathlog/news</c> accept/reject logging.</description></item>
        /// <item><description><see cref="ArticleSpoolPreprocessor"/> — fast header syntax validation and <c>Path</c> mutation.</description></item>
        /// <item><description><see cref="ArticleSpoolPostprocessor"/> — deep header semantics, Message-ID, date, and style checks.</description></item>
        /// <item><description><see cref="NntpSpoolWriterPump"/> — dequeue, preprocess, and atomic disk write loop.</description></item>
        /// <item>
        /// <description>
        /// <see cref="ISpoolWriterScalingPolicy"/> → <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> — queue-depth
        /// writer scaling.
        /// </description>
        /// </item>
        /// <item><description><see cref="NntpSpoolWriterPool"/> — worker task lifecycle and scaling hysteresis.</description></item>
        /// <item>
        /// <description>
        /// <see cref="INntpTransitStorage"/> → <see cref="NntpSpoolTransitStorage"/> — production spool admission for
        /// IHAVE/TAKETHIS handlers.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// <see cref="INntpTransitStorage"/> is registered with
        /// <see cref="ServiceCollectionServiceExtensions.AddSingleton{TService, TImplementation}(IServiceCollection)"/>
        /// so production spool storage wins over any later development-stub fallback registrations in the same collection.
        /// </para>
        /// <para><b>Hosted services:</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// <see cref="NntpSpoolWriterHostedService"/> — starts the writer pool and runs the one-second scaling loop.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <see cref="NntpSpoolStartupConfigLogHostedService"/> — logs resolved spool paths and queue limits once at
        /// startup (EventId 1).
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// <b>Options binding:</b> Do not bind <see cref="NntpServerOptions"/> again in this method. A second
        /// <c>Bind</c> or <c>BindConfiguration</c> pass against the shared <c>NntpServer</c> section appends collection
        /// properties such as <see cref="NntpServerOptions.TransitPeers"/> and fails duplicate-name validation.
        /// </para>
        /// <example>
        /// <code>
        /// builder.Services.AddNntpHistoryDatabase(builder.Configuration);
        /// builder.Services.AddNntpSocketsTransit();
        /// builder.Services.AddNntpArticlesTransitSpool(builder.Configuration);
        /// </code>
        /// </example>
        /// </remarks>
        public static IServiceCollection AddNntpArticlesTransitSpool(
            this IServiceCollection services,
            IConfiguration? configuration = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (configuration is not null)
            {
                _ = services
                    .AddOptions<PostFilterOptions>()
                    .Bind(configuration.GetSection(PostFilterOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                _ = services.AddSpamAssassin(configuration);
                _ = services.AddSingleton<INntpNewsLog>(sp => new NntpNewsLog(configuration));
            }
            else
            {
                _ = services.AddOptions<PostFilterOptions>();
                _ = services
                    .AddOptions<SpamAssassinOptions>()
                    .Configure(options => options.Host = "127.0.0.1");
                _ = services.AddSingleton<IValidateOptions<SpamAssassinOptions>, SpamAssassinOptionsValidator>();
                _ = services.AddSingleton<ISpamAssassin, SpamAssassin>();
                _ = services.AddSingleton<SpamAssassin>();
                _ = services.AddSingleton<INntpNewsLog>(NullNntpNewsLog.Instance);
            }

            _ = services.AddSingleton<NntpSpoolMetrics>();
            _ = services.AddSingleton<NntpSpoolWriteQueue>();
            _ = services.AddSingleton<ArticleSpoolPreprocessor>();
            _ = services.AddSingleton<SpamdScanArticleBuilder>();
            _ = services.AddSingleton<ArticleSpoolPostprocessor>();
            _ = services.AddSingleton<NntpSpoolWriterPump>();
            _ = services.AddSingleton<ISpoolWriterScalingPolicy, ProcessorQueueSpoolWriterScalingPolicy>();
            _ = services.AddSingleton<NntpSpoolWriterPool>();
            _ = services.AddSingleton<INntpTransitStorage, NntpSpoolTransitStorage>();
            _ = services.AddHostedService<NntpSpoolWriterHostedService>();
            _ = services.AddHostedService<NntpSpoolStartupConfigLogHostedService>();

            return services;
        }

        /// <summary>
        /// <see cref="IHostedService"/> that emits one information log with resolved transit spool configuration at host
        /// startup.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Registered privately by <see cref="AddNntpArticlesTransitSpool"/> so operators can confirm effective spool
        /// paths and queue limits without reading raw configuration files. Does not create directories or mutate options.
        /// </para>
        /// <para>
        /// <see cref="StartAsync"/> resolves paths through <see cref="SpoolDirectoryUtilities"/> (canonical absolute
        /// spool root and <see cref="SpoolDirectoryUtilities.IncomingSubdirectory"/>). <see cref="StopAsync"/> is a no-op.
        /// </para>
        /// </remarks>
        private sealed class NntpSpoolStartupConfigLogHostedService : IHostedService
        {
            /// <summary>
            /// Snapshot of bound <see cref="NntpServerOptions"/> captured at construction for startup logging.
            /// </summary>
            /// <remarks>
            /// Taken from <see cref="IOptions{TOptions}.Value"/> once; later options monitor changes are not re-logged by
            /// this service.
            /// </remarks>
            private readonly NntpServerOptions _options;

            /// <summary>
            /// Category logger for the startup configuration event.
            /// </summary>
            private readonly ILogger<NntpSpoolStartupConfigLogHostedService> _logger;

            /// <summary>
            /// Initializes a new instance of the <see cref="NntpSpoolStartupConfigLogHostedService"/> class.
            /// </summary>
            /// <param name="options">Bound server options supplying spool directory and queue settings.</param>
            /// <param name="logger">Logger for the startup information event.</param>
            /// <exception cref="ArgumentNullException">
            /// Thrown when <paramref name="options"/> or <paramref name="logger"/> is <see langword="null"/>.
            /// </exception>
            public NntpSpoolStartupConfigLogHostedService(
                IOptions<NntpServerOptions> options,
                ILogger<NntpSpoolStartupConfigLogHostedService> logger)
            {
                ArgumentNullException.ThrowIfNull(options);
                ArgumentNullException.ThrowIfNull(logger);
                _options = options.Value;
                _logger = logger;
            }

            /// <summary>
            /// Logs resolved spool root, incoming directory, queue capacity, byte budget, and path-append token once.
            /// </summary>
            /// <param name="cancellationToken">Host startup cancellation token (unused).</param>
            /// <returns><see cref="Task.CompletedTask"/> after the information log is written.</returns>
            /// <remarks>
            /// <para>
            /// Delegates to <see cref="NntpSpoolStartupConfigLog.SpoolConfigured"/> at <see cref="LogLevel.Information"/>
            /// (EventId 1) with:
            /// </para>
            /// <list type="bullet">
            /// <item><description><see cref="SpoolDirectoryUtilities.ResolveSpoolDirectory"/> output for spool root.</description></item>
            /// <item><description><see cref="SpoolDirectoryUtilities.GetIncomingDirectory"/> output for incoming path.</description></item>
            /// <item><description><see cref="NntpServerOptions.SpoolQueueCapacity"/>.</description></item>
            /// <item><description><see cref="NntpServerOptions.MaxQueuedBytes"/>.</description></item>
            /// <item><description><see cref="NntpServerOptions.PathAppend"/> (may be empty).</description></item>
            /// </list>
            /// </remarks>
            public Task StartAsync(CancellationToken cancellationToken)
            {
                _ = cancellationToken;
                string spoolRoot = SpoolDirectoryUtilities.ResolveSpoolDirectory(_options);
                string incoming = SpoolDirectoryUtilities.GetIncomingDirectory(spoolRoot);
                NntpSpoolStartupConfigLog.SpoolConfigured(
                    _logger,
                    spoolRoot,
                    incoming,
                    _options.SpoolQueueCapacity,
                    _options.MaxQueuedBytes,
                    _options.PathAppend);
                return Task.CompletedTask;
            }

            /// <summary>
            /// No-op host stop hook; startup logging requires no teardown.
            /// </summary>
            /// <param name="cancellationToken">Host stop cancellation token (unused).</param>
            /// <returns><see cref="Task.CompletedTask"/>.</returns>
            public Task StopAsync(CancellationToken cancellationToken)
            {
                _ = cancellationToken;
                return Task.CompletedTask;
            }
        }
    }
}
