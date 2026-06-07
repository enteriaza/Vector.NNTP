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
    /// Optionally mirrors RocksDB statistics into the NNTPD host logger on a fixed interval.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not required on RocksDB 10.x.</b> With <see cref="HistoryRocksDbOptions.EnableStatistics"/> and
    /// <see cref="HistoryRocksDbOptions.StatsDumpPeriodSec"/>, the native library already writes
    /// <c>------- DUMPING STATS -------</c> / <c>------- PERSISTING STATS -------</c> sections to <c>DbDir/LOG</c>
    /// every interval. That path was unreliable on the prior RocksDbSharp 6.2.2 runtime; this hosted service was the
    /// workaround to surface <c>rocksdb.stats</c> and ticker text in the host log pipeline.
    /// </para>
    /// <para>
    /// Runs only when <see cref="HistoryRocksDbOptions.MirrorStatsToHostLogger"/> is <see langword="true"/> (default
    /// <see langword="false"/>). Operators tailing <c>DbDir/LOG</c> on 10.x can leave mirroring disabled.
    /// </para>
    /// </remarks>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class HistoryRocksStatsLogHostedService(
        ILogger<HistoryRocksStatsLogHostedService> logger) : BackgroundService
    {
        /// <summary>
        /// Operational gate; stats mirroring runs only after history startup completes.
        /// </summary>
        private readonly HistoryDatabaseService _history = null!;

        /// <summary>
        /// RocksDB store supplying native statistics text for host logger mirroring.
        /// </summary>
        private readonly RocksHistoryStore _rocks = null!;

        /// <summary>
        /// Bound history options controlling mirror interval and Rocks statistics flags.
        /// </summary>
        private readonly HistoryDbOptions _options = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryRocksStatsLogHostedService"/> class.
        /// </summary>
        /// <param name="history">History service (operational gate).</param>
        /// <param name="rocks">Rocks store.</param>
        /// <param name="options">History options.</param>
        /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
        public HistoryRocksStatsLogHostedService(
            HistoryDatabaseService history,
            RocksHistoryStore rocks,
            IOptions<HistoryDbOptions> options,
            ILogger<HistoryRocksStatsLogHostedService> logger)
            : this(logger)
        {
            _history = history;
            _rocks = rocks;
            _options = options.Value;
        }

        /// <summary>
        /// Periodically logs RocksDB statistics to the host logger when mirroring is enabled.
        /// </summary>
        /// <param name="stoppingToken">Host shutdown token; cancels the wait between mirror cycles.</param>
        /// <returns>A task that exits immediately when mirroring is disabled, otherwise runs until shutdown.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="stoppingToken"/> is canceled during timer wait.</exception>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            HistoryRocksDbOptions rocks = _options.RocksDb;
            if (!rocks.MirrorStatsToHostLogger ||
                !rocks.EnableStatistics ||
                rocks.StatsDumpPeriodSec == 0)
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
