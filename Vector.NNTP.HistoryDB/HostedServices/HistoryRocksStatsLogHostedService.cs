// <copyright file="HistoryRocksStatsLogHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;

namespace Vector.NNTP.HistoryDB.HostedServices
{
    /// <summary>
    /// Emits RocksDB statistics on a fixed interval to the host logger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SetStatsDumpPeriodSec</c> on RocksDB <c>DbOptions</c> is passed through to RocksDB, but RocksDbSharp 6.2.x /
    /// bundled native builds often do not emit periodic <c>LOG</c> dumps even when statistics are enabled. This service
    /// queries <c>rocksdb.stats</c> and ticker statistics explicitly so operators get predictable snapshots.
    /// </para>
    /// </remarks>
    internal sealed class HistoryRocksStatsLogHostedService : BackgroundService
    {
        private readonly HistoryDatabaseService _history;
        private readonly RocksHistoryStore _rocks;
        private readonly HistoryDbOptions _options;
        private readonly ILogger<HistoryRocksStatsLogHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryRocksStatsLogHostedService"/> class.
        /// </summary>
        /// <param name="history">History service (operational gate).</param>
        /// <param name="rocks">Rocks store.</param>
        /// <param name="options">History options.</param>
        /// <param name="logger">Logger.</param>
        public HistoryRocksStatsLogHostedService(
            HistoryDatabaseService history,
            RocksHistoryStore rocks,
            IOptions<HistoryDbOptions> options,
            ILogger<HistoryRocksStatsLogHostedService> logger)
        {
            this._history = history;
            this._rocks = rocks;
            this._options = options.Value;
            this._logger = logger;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            HistoryRocksDbOptions rocks = this._options.RocksDb;
            if (!rocks.EnableStatistics || rocks.StatsDumpPeriodSec == 0)
            {
                return;
            }

            TimeSpan period = TimeSpan.FromSeconds(rocks.StatsDumpPeriodSec);
            using PeriodicTimer timer = new(period);
            this._logger.LogDebug(
                "HistoryDB host Rocks stats logging every {PeriodSeconds}s (native LOG dumps may still be absent on RocksDbSharp 6.2.x)",
                rocks.StatsDumpPeriodSec);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!this._history.IsOperational)
                {
                    continue;
                }

                try
                {
                    this._rocks.EmitStatsSnapshot();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    this._logger.LogError(ex, "HistoryDB Rocks stats snapshot failed");
                }
            }
        }
    }
}
