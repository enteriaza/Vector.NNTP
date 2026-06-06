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
    internal sealed partial class HistoryBackgroundWorkerHostedService : BackgroundService
    {
        /// <summary>
        /// The history service.
        /// </summary>
        private readonly HistoryDatabaseService _history;

        /// <summary>
        /// The Rocks store.
        /// </summary>
        private readonly RocksHistoryStore _rocks;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<HistoryBackgroundWorkerHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryBackgroundWorkerHostedService"/> class.
        /// </summary>
        /// <param name="history">History service.</param>
        /// <param name="rocks">Rocks store.</param>
        /// <param name="logger">Logger.</param>
        internal HistoryBackgroundWorkerHostedService(
            HistoryDatabaseService history,
            RocksHistoryStore rocks,
            ILogger<HistoryBackgroundWorkerHostedService> logger)
        {
            _history = history;
            _rocks = rocks;
            _logger = logger;
        }

        /// <summary>
        /// Executes the background worker.
        /// </summary>
        /// <param name="stoppingToken">Stopping token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the stopping token is canceled.</exception>
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
