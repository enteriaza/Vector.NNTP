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
    /// <b>Role:</b> Operator-facing rollup of accept and categorized reject rates per minute. Complements
    /// <see cref="Logging.INntpNewsLog"/> (per-article INN lines on the <c>news</c> file) and OpenTelemetry counters on
    /// <see cref="NntpSpoolMetrics"/> by summarizing the same outcome buckets into readable host log lines. This service
    /// only <em>reads</em> metrics via <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/>; it does not record
    /// accepts or rejections.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/> alongside
    /// <see cref="NntpSpoolWriterHostedService"/> (writer pool) and the one-shot spool configuration log hosted service.
    /// Shares the <see cref="NntpSpoolMetrics"/> singleton with writer pumps and
    /// <see cref="Storage.NntpSpoolTransitStorage"/>.
    /// </para>
    /// <para><b>Execute loop (each minute):</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <see cref="PeriodicTimer"/> with <see cref="LogInterval"/> — the first tick fires after one minute, not immediately
    /// at host start.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> — drains per-feed minute buckets, builds global rollup,
    /// resets counters for the next window.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolThroughputLog.EmitSnapshot"/> — writes EventId <c>1</c> global and EventId <c>2</c> per-feed
    /// Information lines when the global rollup has activity (see logging partial remarks).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Output channel:</b> Host <see cref="ILogger"/> pipeline (for example <c>NNTPD-.log</c> and console), not the
    /// dedicated INN <c>news-{date}.log</c> Serilog sink.
    /// </para>
    /// <para>
    /// <b>Fault tolerance:</b> Unexpected failures during snapshot capture or emission are caught, logged at Error EventId
    /// <c>3</c> via <see cref="LogSnapshotFailed"/>, and the minute loop continues. <see cref="OperationCanceledException"/>
    /// from host shutdown is not caught and ends the execute task.
    /// </para>
    /// <para>
    /// <b>Partial class:</b> Source-generated throughput formatters (EventIds <c>1</c>–<c>2</c>) live in
    /// <c>NntpSpoolThroughputLogHostedService.Logging.cs</c> as <see cref="NntpSpoolThroughputLog"/>.
    /// </para>
    /// <para><b>Threading:</b> Runs on the BackgroundService execute task; safe relative to concurrent metric writers.</para>
    /// </remarks>
    internal sealed partial class NntpSpoolThroughputLogHostedService : BackgroundService
    {
        /// <summary>
        /// Interval between throughput summary log emissions.
        /// </summary>
        /// <value>One minute.</value>
        /// <remarks>
        /// Passed to <see cref="PeriodicTimer"/> in <see cref="ExecuteAsync"/>. Aligns with the minute buckets drained by
        /// <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/>.
        /// </remarks>
        private static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Shared spool metrics singleton supplying minute accept/reject deltas.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Same instance used by <see cref="Storage.NntpSpoolWriterPump"/> and
        /// <see cref="Storage.NntpSpoolTransitStorage"/> to record outcomes. This hosted service is the sole caller of
        /// <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/>.
        /// </para>
        /// </remarks>
        private readonly NntpSpoolMetrics _metrics;

        /// <summary>
        /// Host application category logger for throughput summary and snapshot-failure lines.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Typed as <see cref="ILogger{NntpSpoolThroughputLogHostedService}"/> for category filtering in host Serilog
        /// configuration. Must not be <see cref="Logging.INntpNewsLog"/> or the INN news file sink.
        /// </para>
        /// </remarks>
        private readonly ILogger<NntpSpoolThroughputLogHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolThroughputLogHostedService"/> class.
        /// </summary>
        /// <param name="metrics">
        /// Shared <see cref="NntpSpoolMetrics"/> singleton registered by
        /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
        /// </param>
        /// <param name="logger">
        /// Host category logger for minute throughput summaries (main application log, not INN <c>news</c>).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="metrics"/> or <paramref name="logger"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Does not start the timer loop; <see cref="ExecuteAsync"/> runs after the host starts this
        /// <see cref="BackgroundService"/>.
        /// </remarks>
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
        /// Periodically drains spool throughput minute buckets and logs summaries until host shutdown.
        /// </summary>
        /// <param name="stoppingToken">
        /// BackgroundService lifetime token. Cancellation ends waits in
        /// <see cref="PeriodicTimer.WaitForNextTickAsync"/> and completes the execute task.
        /// </param>
        /// <returns>
        /// A task that runs for the hosted-service lifetime. Completes when <paramref name="stoppingToken"/> is canceled
        /// (typically via <see cref="OperationCanceledException"/> from the timer wait).
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// Propagated when <paramref name="stoppingToken"/> is canceled during
        /// <see cref="PeriodicTimer.WaitForNextTickAsync"/>. Not caught by the per-tick try/catch.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Each timer tick calls <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> then
        /// <see cref="NntpSpoolThroughputLog.EmitSnapshot"/>. Idle minutes (global
        /// <see cref="SpoolThroughputFeedCounts.Processed"/> zero) produce no Information lines.
        /// </para>
        /// <para>
        /// <b>Fail-open:</b> Any other exception in the tick body is logged through <see cref="LogSnapshotFailed"/> and
        /// suppressed so the next minute can still run. <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> is
        /// documented never to throw; failures are unexpected implementation or environment defects.
        /// </para>
        /// </remarks>
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
        /// Logs an unexpected failure while capturing or emitting a throughput minute snapshot.
        /// </summary>
        /// <param name="logger">Host category logger (same instance as <see cref="_logger"/> on the execute path).</param>
        /// <param name="exception">
        /// Exception thrown from <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> or
        /// <see cref="NntpSpoolThroughputLog.EmitSnapshot"/>. Recorded as the structured exception on the log event.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated by <see cref="LoggerMessageAttribute"/> at EventId <c>3</c>, <see cref="LogLevel.Error"/>.
        /// Message: <c>Failed to emit spool throughput minute summary.</c>
        /// </para>
        /// <para>
        /// Invoked from the catch block in <see cref="ExecuteAsync"/> only for exceptions other than
        /// <see cref="OperationCanceledException"/>. Does not rethrow; the minute loop continues on the next timer tick.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Error,
            Message = "Failed to emit spool throughput minute summary.")]
        private static partial void LogSnapshotFailed(ILogger logger, Exception exception);
    }
}
