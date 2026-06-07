// <copyright file="HistoryBackgroundWorkerHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;
using Vector.NNTP.HistoryDB.Telemetry;

namespace Vector.NNTP.HistoryDB.HostedServices
{
    /// <summary>
    /// Runs periodic RocksDB expiry sweep (CHECK persist is handled by <see cref="HistoryRocksPersistPump"/>).
    /// </summary>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class HistoryBackgroundWorkerHostedService(
        ILogger<HistoryBackgroundWorkerHostedService> logger) : BackgroundService
    {
        /// <summary>
        /// Operational gate and queue depth source for the expiry sweep loop.
        /// </summary>
        private readonly HistoryDatabaseService _history = null!;

        /// <summary>
        /// RocksDB store receiving periodic expiration-key purge from this worker.
        /// </summary>
        private readonly RocksHistoryStore _rocks = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryBackgroundWorkerHostedService"/> class.
        /// </summary>
        /// <param name="history">History service.</param>
        /// <param name="rocks">Rocks store.</param>
        /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
        public HistoryBackgroundWorkerHostedService(
            HistoryDatabaseService history,
            RocksHistoryStore rocks,
            ILogger<HistoryBackgroundWorkerHostedService> logger)
            : this(logger)
        {
            _history = history;
            _rocks = rocks;
        }

        /// <summary>
        /// Runs a five-minute periodic timer that sweeps expired Rocks keys when history is operational.
        /// </summary>
        /// <param name="stoppingToken">Host shutdown token; cancels the timer wait between sweeps.</param>
        /// <returns>A task that runs until <paramref name="stoppingToken"/> is canceled.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="stoppingToken"/> is canceled during timer wait.</exception>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_history.IsOperational)
                {
                    continue;
                }

                using Activity? activity = HistoryDbTelemetry.ActivitySource.StartActivity("history.rocks.sweep");
                long sweepStartTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    int deleted = _rocks.SweepExpired(now, maxDeletes: 10_000);
                    double elapsedMs = Stopwatch.GetElapsedTime(sweepStartTimestamp).TotalMilliseconds;
                    _ = (activity?.SetTag("history.sweep.deleted", deleted));
                    _ = (activity?.SetTag("history.sweep.duration_ms", elapsedMs));
                    if (deleted > 0)
                    {
                        LogSweepCompleted(deleted, elapsedMs);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSweepFailed(ex);
                    _ = (activity?.SetStatus(ActivityStatusCode.Error, ex.Message));
                }
            }
        }
    }
}
