// <copyright file="HistoryBackgroundWorkerHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;

namespace Vector.NNTP.HistoryDB.HostedServices
{
    /// <summary>
    /// Runs periodic RocksDB expiry sweep (CHECK persist is handled by <see cref="HistoryRocksPersistPump"/>).
    /// </summary>
    internal sealed class HistoryBackgroundWorkerHostedService : BackgroundService
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
        public HistoryBackgroundWorkerHostedService(
            HistoryDatabaseService history,
            RocksHistoryStore rocks,
            ILogger<HistoryBackgroundWorkerHostedService> logger)
        {
            this._history = history;
            this._rocks = rocks;
            this._logger = logger;
        }

        /// <summary>
        /// Executes the background worker.
        /// </summary>
        /// <param name="stoppingToken">Stopping token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!this._history.IsOperational)
                {
                    continue;
                }

                try
                {
                    ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    _ = this._rocks.SweepExpired(now, maxDeletes: 10_000);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    this._logger.LogError(ex, "History RocksDB sweep failed");
                }
            }
        }
    }
}
