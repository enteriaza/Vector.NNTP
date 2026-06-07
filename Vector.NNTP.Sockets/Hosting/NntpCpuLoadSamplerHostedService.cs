// <copyright file="NntpCpuLoadSamplerHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: periodic CPU utilization sampling for overload gating.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.Metrics;

namespace Vector.NNTP.Sockets.Hosting
{
    /// <summary>
    /// Periodically samples CPU utilization and updates the hysteresis overload gate.
    /// </summary>
    public sealed partial class NntpCpuLoadSamplerHostedService : BackgroundService
    {
        private readonly INntpCpuLoadMonitor _monitor;
        private readonly IOptionsMonitor<NntpServerOptions> _options;
        private readonly ILogger<NntpCpuLoadSamplerHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpCpuLoadSamplerHostedService"/> class.
        /// </summary>
        /// <param name="monitor">CPU load monitor.</param>
        /// <param name="options">Server options.</param>
        /// <param name="logger">Logger.</param>
        public NntpCpuLoadSamplerHostedService(
            INntpCpuLoadMonitor monitor,
            IOptionsMonitor<NntpServerOptions> options,
            ILogger<NntpCpuLoadSamplerHostedService> logger)
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                NntpServerOptions opts = _options.CurrentValue;
                TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, opts.CpuSamplingIntervalSeconds));
                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    _monitor.RecordSample();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSamplerFailure(ex);
                }
            }
        }

        /// <summary>
        /// Logs a sampler failure without crashing the host.
        /// </summary>
        /// <param name="exception">Observed exception.</param>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "CPU utilization sampler failed; gate state unchanged until next successful sample")]
        private partial void LogSamplerFailure(Exception exception);
    }
}
