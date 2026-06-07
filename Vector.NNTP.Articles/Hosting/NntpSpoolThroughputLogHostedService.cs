// <copyright file="NntpSpoolThroughputLogHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: once-per-minute spool throughput summaries to the main host application log.

using Microsoft.Extensions.Hosting;
using Vector.NNTP.Articles.Metrics;

namespace Vector.NNTP.Articles.Hosting
{
    /// <summary>
    /// <see cref="BackgroundService"/> that emits single-line spool throughput summaries to the main host logger every minute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered by <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// Reads accept/reject deltas from <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> and writes one global
    /// line plus one line per active feed through <see cref="NntpSpoolThroughputLog.EmitSnapshot"/>.
    /// </para>
    /// <para>
    /// Output goes to the host <see cref="ILogger"/> pipeline (<c>NNTPD-.log</c>), not the dedicated INN
    /// <c>news-{date}.log</c> Serilog sink.
    /// </para>
    /// </remarks>
    internal sealed partial class NntpSpoolThroughputLogHostedService : BackgroundService
    {
        /// <summary>
        /// Interval between throughput summary log emissions.
        /// </summary>
        private static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Spool metrics supplying minute accept/reject deltas.
        /// </summary>
        private readonly NntpSpoolMetrics _metrics;

        /// <summary>
        /// Host application logger for throughput summaries.
        /// </summary>
        private readonly ILogger<NntpSpoolThroughputLogHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolThroughputLogHostedService"/> class.
        /// </summary>
        /// <param name="metrics">Shared spool metrics singleton.</param>
        /// <param name="logger">Host category logger (main application log).</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="metrics"/> or <paramref name="logger"/> is <see langword="null"/>.
        /// </exception>
        public NntpSpoolThroughputLogHostedService(
            NntpSpoolMetrics metrics,
            ILogger<NntpSpoolThroughputLogHostedService> logger)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(logger);
            _metrics = metrics;
            _logger = logger;
        }

        /// <summary>
        /// Periodically logs spool throughput deltas until host shutdown.
        /// </summary>
        /// <param name="stoppingToken">Host shutdown token.</param>
        /// <returns>A task that runs until cancellation.</returns>
        /// <exception cref="OperationCanceledException">
        /// Propagated when <paramref name="stoppingToken"/> is canceled during timer wait.
        /// </exception>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new(LogInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    SpoolThroughputMinuteSnapshot snapshot = _metrics.TakeMinuteSnapshotAndReset();
                    NntpSpoolThroughputLog.EmitSnapshot(_logger, snapshot);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSnapshotFailed(_logger, ex);
                }
            }
        }

        /// <summary>
        /// Logs an unexpected failure while capturing or emitting a throughput snapshot.
        /// </summary>
        /// <param name="logger">Host category logger.</param>
        /// <param name="exception">Failure preventing snapshot emission.</param>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Error,
            Message = "Failed to emit spool throughput minute summary.")]
        private static partial void LogSnapshotFailed(ILogger logger, Exception exception);
    }
}
