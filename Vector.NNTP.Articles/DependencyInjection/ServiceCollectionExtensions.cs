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
    /// <b>Role:</b> Articles-layer composition root for MODE STREAM transit spool persistence. Hosts call
    /// <see cref="AddNntpArticlesTransitSpool"/> after socket and history services are registered so
    /// <see cref="IOptions{NntpServerOptions}"/> and <see cref="HistoryDB.Abstractions.IHistoryDatabase"/> resolve for
    /// spool components. The NNTPD transit host wires this immediately after <c>AddNntpSocketsTransit</c> in
    /// <c>Program.Nntp.cs</c>.
    /// </para>
    /// <para>
    /// <b>Options ownership:</b> This extension does not bind <see cref="NntpServerOptions"/> itself. Spool settings such
    /// as <see cref="NntpServerOptions.SpoolDir"/>, <see cref="NntpServerOptions.SpoolQueueCapacity"/>, and
    /// <see cref="NntpServerOptions.MaxQueuedBytes"/> must come from the host's existing options registration. Rebidding
    /// the shared <c>NntpServer</c> section here would duplicate collection properties and fail validation.
    /// </para>
    /// <para><b>Nested types:</b> <see cref="NntpSpoolStartupConfigLogHostedService"/> is a private hosted service
    /// registered only through <see cref="AddNntpArticlesTransitSpool"/>; its logging partial lives in
    /// <c>NntpSpoolStartupConfigLogHostedService.Logging.cs</c> as <see cref="NntpSpoolStartupConfigLog"/>.</para>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the transit spool pipeline, production <see cref="INntpTransitStorage"/>, filter dependencies, and
        /// hosted writer/metrics services.
        /// </summary>
        /// <param name="services">Service collection to extend. Must not be <see langword="null"/>.</param>
        /// <param name="configuration">
        /// Host configuration root. When non-<see langword="null"/>, binds <see cref="PostFilterOptions"/> and registers
        /// production <see cref="NntpNewsLog"/> and <see cref="Filters.DependencyInjection.ServiceCollectionExtensions.AddSpamAssassin"/>.
        /// Pass <see langword="null"/> in unit tests for lightweight stubs (<see cref="NullNntpNewsLog"/> and a minimal
        /// in-process <see cref="SpamAssassin"/> client).
        /// </param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="services"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para><b>Prerequisites (host must register first):</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// <see cref="IOptions{NntpServerOptions}"/> (or <see cref="IOptionsMonitor{NntpServerOptions}"/>) — typically via
        /// <c>AddNntpSocketsTransit</c>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <see cref="HistoryDB.Abstractions.IHistoryDatabase"/> from <c>AddNntpHistoryDatabase</c> —
        /// <see cref="NntpSpoolWriterPump"/> releases HistoryDB reservations on preprocess/postprocess/write failure.
        /// </description>
        /// </item>
        /// <item><description>Host logging (<see cref="ILogger{T}"/> categories resolve automatically).</description></item>
        /// </list>
        /// <para><b>Configuration branch (<paramref name="configuration"/> is not <see langword="null"/>):</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// <see cref="PostFilterOptions"/> — bound from <see cref="PostFilterOptions.SectionName"/> with data annotations
        /// and <c>ValidateOnStart</c> (used by <see cref="ArticleSpoolPostprocessor"/> for style rules).
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <see cref="SpamAssassinOptions"/> — registered through <see cref="Filters.DependencyInjection.ServiceCollectionExtensions.AddSpamAssassin"/>
        /// with full validation and <c>ValidateOnStart</c>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <see cref="INntpNewsLog"/> → <see cref="NntpNewsLog"/> factory using host configuration for
        /// <c>Logging:LogDir</c>.
        /// </description>
        /// </item>
        /// </list>
        /// <para><b>Test branch (<paramref name="configuration"/> is <see langword="null"/>):</b></para>
        /// <list type="bullet">
        /// <item><description><see cref="PostFilterOptions"/> — default options instance without configuration binding.</description></item>
        /// <item>
        /// <description>
        /// <see cref="SpamAssassinOptions"/> — default with <c>Host = 127.0.0.1</c> plus manual
        /// <see cref="ISpamAssassin"/> / <see cref="SpamAssassin"/> singletons (no <c>ValidateOnStart</c>).
        /// </description>
        /// </item>
        /// <item><description><see cref="INntpNewsLog"/> → <see cref="NullNntpNewsLog.Instance"/>.</description></item>
        /// </list>
        /// <para><b>Singleton registrations (both branches):</b></para>
        /// <list type="bullet">
        /// <item><description><see cref="NntpSpoolMetrics"/> — OpenTelemetry counters/gauges and minute throughput buckets.</description></item>
        /// <item><description><see cref="NntpSpoolWriteQueue"/> — bounded in-memory transit queue.</description></item>
        /// <item><description><see cref="ArticleSpoolPreprocessor"/> — header syntax validation and <c>Path</c> mutation.</description></item>
        /// <item><description><see cref="SpamdScanArticleBuilder"/> — synthetic scan article bytes for SpamAssassin.</description></item>
        /// <item><description><see cref="ArticleSpoolPostprocessor"/> — deep header checks, style rules, optional spam scan.</description></item>
        /// <item><description><see cref="NntpSpoolWriterPump"/> — dequeue, preprocess/postprocess, atomic disk write.</description></item>
        /// <item>
        /// <description>
        /// <see cref="ISpoolWriterScalingPolicy"/> → <see cref="ProcessorQueueSpoolWriterScalingPolicy"/>.
        /// </description>
        /// </item>
        /// <item><description><see cref="NntpSpoolWriterPool"/> — writer worker lifecycle and scaling hysteresis.</description></item>
        /// <item>
        /// <description>
        /// <see cref="INntpTransitStorage"/> → <see cref="NntpSpoolTransitStorage"/> — production spool admission for
        /// IHAVE/TAKETHIS handlers.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// <see cref="INntpTransitStorage"/> uses explicit implementation registration so production spool storage wins
        /// over any later development-stub fallback registrations in the same service collection.
        /// </para>
        /// <para><b>Hosted services:</b></para>
        /// <list type="bullet">
        /// <item><description><see cref="NntpSpoolWriterHostedService"/> — pool start and one-second writer scaling loop.</description></item>
        /// <item><description><see cref="NntpSpoolStartupConfigLogHostedService"/> — one-shot spool configuration log (EventId 1).</description></item>
        /// <item><description><see cref="NntpSpoolThroughputLogHostedService"/> — minute throughput summaries to the host log.</description></item>
        /// </list>
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
            _ = services.AddHostedService<NntpSpoolThroughputLogHostedService>();

            return services;
        }

        /// <summary>
        /// Private <see cref="IHostedService"/> that emits one Information log with resolved transit spool configuration at
        /// host startup.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Registered only by <see cref="AddNntpArticlesTransitSpool"/> so operators can confirm effective spool paths and
        /// queue limits without reading raw configuration files. Does not create directories, mutate options, or re-log on
        /// options monitor changes.
        /// </para>
        /// <para>
        /// <see cref="StartAsync"/> resolves paths through
        /// <see cref="SpoolDirectoryUtilities"/> and delegates formatting to
        /// <see cref="NntpSpoolStartupConfigLog.SpoolConfigured"/>. <see cref="StopAsync"/>
        /// is a no-op.
        /// </para>
        /// </remarks>
        private sealed class NntpSpoolStartupConfigLogHostedService : IHostedService
        {
            /// <summary>
            /// Snapshot of bound <see cref="NntpServerOptions"/> captured at construction for startup logging.
            /// </summary>
            /// <remarks>
            /// Taken from <see cref="IOptions{TOptions}.Value"/> once in the constructor. Later
            /// <see cref="IOptionsMonitor{TOptions}"/> changes are not reflected in a second startup log event.
            /// </remarks>
            private readonly NntpServerOptions _options;

            /// <summary>
            /// Category logger for the startup configuration event (EventId <c>1</c>).
            /// </summary>
            private readonly ILogger<NntpSpoolStartupConfigLogHostedService> _logger;

            /// <summary>
            /// Initializes a new instance of the <see cref="NntpSpoolStartupConfigLogHostedService"/> class.
            /// </summary>
            /// <param name="options">
            /// Bound server options supplying <see cref="NntpServerOptions.SpoolDir"/>,
            /// <see cref="NntpServerOptions.SpoolQueueCapacity"/>, <see cref="NntpServerOptions.MaxQueuedBytes"/>, and
            /// <see cref="NntpServerOptions.PathAppend"/>.
            /// </param>
            /// <param name="logger">Logger for <see cref="NntpSpoolStartupConfigLog.SpoolConfigured"/>.</param>
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
            /// <param name="cancellationToken">
            /// Host startup cancellation token. Unused; work is synchronous and completes before returning.
            /// </param>
            /// <returns><see cref="Task.CompletedTask"/> after the Information log is written.</returns>
            /// <remarks>
            /// <para>
            /// Invokes <see cref="NntpSpoolStartupConfigLog.SpoolConfigured"/> at <see cref="LogLevel.Information"/>
            /// (EventId <c>1</c>) with:
            /// </para>
            /// <list type="bullet">
            /// <item><description><see cref="SpoolDirectoryUtilities.ResolveSpoolDirectory"/> output for spool root.</description></item>
            /// <item><description><see cref="SpoolDirectoryUtilities.GetIncomingDirectory"/> output for incoming path.</description></item>
            /// <item><description><see cref="NntpServerOptions.SpoolQueueCapacity"/>.</description></item>
            /// <item><description><see cref="NntpServerOptions.MaxQueuedBytes"/>.</description></item>
            /// <item>
            /// <description>
            /// <see cref="NntpServerOptions.PathAppend"/> (may be null or empty when path mutation is disabled).
            /// </description>
            /// </item>
            /// </list>
            /// <para>
            /// Runs during host startup before <see cref="NntpSpoolWriterHostedService"/> begins dequeuing. Never throws
            /// for normal options and path resolution inputs.
            /// </para>
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
            /// <param name="cancellationToken">Host stop cancellation token. Unused.</param>
            /// <returns><see cref="Task.CompletedTask"/>.</returns>
            /// <remarks>Does not dispose log sinks or alter spool directories.</remarks>
            public Task StopAsync(CancellationToken cancellationToken)
            {
                _ = cancellationToken;
                return Task.CompletedTask;
            }
        }
    }
}
