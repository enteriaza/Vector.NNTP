// <copyright file="HistoryRocksPersistPump.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Telemetry;

namespace Vector.NNTP.HistoryDB.Services
{
    /// <summary>
    /// Drains the CHECK persist queue into RocksDB; started when HistoryDB becomes operational.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Started from <see cref="HistoryDatabaseService.SetOperational"/> so the pump is guaranteed to run
    /// before CHECK accepts traffic, independent of generic-host background-service scheduling order.
    /// </para>
    /// </remarks>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class HistoryRocksPersistPump(
        ILogger<HistoryRocksPersistPump> logger)
    {
        /// <summary>
        /// Slow-call logging threshold for persist milestones (every N items).
        /// </summary>
        private const long PersistMilestoneInterval = 10_000;

        /// <summary>
        /// The RocksDB store.
        /// </summary>
        private readonly RocksHistoryStore _rocks = null!;

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics = null!;

        /// <summary>
        /// The options.
        /// </summary>
        private readonly HistoryDbOptions _options = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryRocksPersistPump"/> class.
        /// </summary>
        /// <param name="rocks">Rocks store.</param>
        /// <param name="metrics">Metrics.</param>
        /// <param name="options">History options.</param>
        /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
        public HistoryRocksPersistPump(
            RocksHistoryStore rocks,
            HistoryMetrics metrics,
            IOptions<HistoryDbOptions> options,
            ILogger<HistoryRocksPersistPump> logger)
            : this(logger)
        {
            _rocks = rocks;
            _metrics = metrics;
            _options = options.Value;
        }

        /// <summary>
        /// Whether the pump has started.
        /// </summary>
        private int _started;

        /// <summary>
        /// The total number of persisted items.
        /// </summary>
        private long _totalPersisted;

        /// <summary>
        /// Starts the persist loop once (idempotent).
        /// </summary>
        /// <param name="reader">Persist queue reader.</param>
        /// <param name="hostStopping">Host shutdown token.</param>
        /// <param name="history">History service for queue depth updates.</param>
        internal void Start(
            ChannelReader<HistoryPersistItem> reader,
            CancellationToken hostStopping,
            HistoryDatabaseService history)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(history);
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            {
                return;
            }

            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(hostStopping);
            CancellationToken token = linkedCts.Token;
            _ = Task.Run(() => RunPersistLoopAsync(reader, history, token), CancellationToken.None);
            LogPumpStarted(Path.GetFullPath(_options.DbDir));
        }

        /// <summary>
        /// Drains the persist queue into RocksDB.
        /// </summary>
        /// <param name="reader">Persist queue reader.</param>
        /// <param name="history">History service for queue depth updates.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private async Task RunPersistLoopAsync(
            ChannelReader<HistoryPersistItem> reader,
            HistoryDatabaseService history,
            CancellationToken cancellationToken)
        {
            using Activity? activity = HistoryDbTelemetry.ActivitySource.StartActivity("history.rocks.persist");
            try
            {
                await foreach (HistoryPersistItem item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        _rocks.PutReservation(item.Digest, item.ExpirationEpochSeconds);
                        _metrics.RecordRocksPersist();
                        history.NotifyPersistDequeued();
                        long count = Interlocked.Increment(ref _totalPersisted);
                        if (count == 1)
                        {
                            LogFirstPersist(Path.GetFullPath(_options.DbDir));
                        }
                        else if (count % PersistMilestoneInterval == 0)
                        {
                            LogPersistMilestone(count);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _metrics.RecordPersistFailure();
                        LogPersistItemFailed(ex);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogPumpStopped();
            }
            catch (Exception ex)
            {
                LogPumpFatal(ex);
                throw;
            }
        }
    }
}
