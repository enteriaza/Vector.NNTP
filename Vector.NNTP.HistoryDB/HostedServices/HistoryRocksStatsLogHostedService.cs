// <copyright file="HistoryRocksStatsLogHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Hosting;
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
    /// <c>SetStatsDumpPeriodSec</c> on RocksDB <c>DbOptions</c> enables native periodic <c>LOG</c> dumps when statistics
    /// are enabled. This service also queries <c>rocksdb.stats</c> and ticker statistics explicitly so operators receive
    /// predictable snapshots in the host logger regardless of native LOG verbosity.
    /// </para>
    /// </remarks>
    /// <param name="history">History service (operational gate).</param>
    /// <param name="rocks">Rocks store.</param>
    /// <param name="options">History options.</param>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class HistoryRocksStatsLogHostedService(
        HistoryDatabaseService history,
        RocksHistoryStore rocks,
        IOptions<HistoryDbOptions> options,
        ILogger<HistoryRocksStatsLogHostedService> logger) : BackgroundService
    {
        /// <summary>
        /// The history database service.
        /// </summary>
        private readonly HistoryDatabaseService _history = history;

        /// <summary>
        /// The rocks history store.
        /// </summary>
        private readonly RocksHistoryStore _rocks = rocks;

        /// <summary>
        /// The history database options.
        /// </summary>
        private readonly HistoryDbOptions _options = options.Value;

        /// <summary>
        /// Executes the hosted service.
        /// </summary>
        /// <param name="stoppingToken">The stopping token.</param>
        /// <returns>A task that completes when the hosted service is executed.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the stopping token is canceled.</exception>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            HistoryRocksDbOptions rocks = _options.RocksDb;
            if (!rocks.EnableStatistics || rocks.StatsDumpPeriodSec == 0)
            {
                return;
            }

            TimeSpan period = TimeSpan.FromSeconds(rocks.StatsDumpPeriodSec);
            using PeriodicTimer timer = new(period);
            LogStatsInterval(rocks.StatsDumpPeriodSec);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_history.IsOperational)
                {
                    continue;
                }

                try
                {
                    _rocks.EmitStatsSnapshot();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogStatsSnapshotFailed(ex);
                }
            }
        }
    }
}
