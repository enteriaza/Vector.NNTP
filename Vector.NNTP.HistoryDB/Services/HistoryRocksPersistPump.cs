// <copyright file="HistoryRocksPersistPump.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Rocks;

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
    internal sealed class HistoryRocksPersistPump
    {
        /// <summary>
        /// The RocksDB store.
        /// </summary>
        private readonly RocksHistoryStore _rocks;

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics;

        /// <summary>
        /// The options.
        /// </summary>
        private readonly HistoryDbOptions _options;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<HistoryRocksPersistPump> _logger;

        /// <summary>
        /// Whether the pump has started.
        /// </summary>
        private int _started;

        /// <summary>
        /// The persist task.
        /// </summary>
        private Task? _persistTask;

        /// <summary>
        /// The linked cancellation token source.
        /// </summary>
        private CancellationTokenSource? _linkedCts;

        /// <summary>
        /// The total number of persisted items.
        /// </summary>
        private long _totalPersisted;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryRocksPersistPump"/> class.
        /// </summary>
        /// <param name="rocks">Rocks store.</param>
        /// <param name="metrics">Metrics.</param>
        /// <param name="options">History options.</param>
        /// <param name="logger">Logger.</param>
        public HistoryRocksPersistPump(
            RocksHistoryStore rocks,
            HistoryMetrics metrics,
            IOptions<HistoryDbOptions> options,
            ILogger<HistoryRocksPersistPump> logger)
        {
            this._rocks = rocks;
            this._metrics = metrics;
            this._options = options.Value;
            this._logger = logger;
        }

        /// <summary>
        /// Starts the persist loop once (idempotent).
        /// </summary>
        /// <param name="reader">Persist queue reader.</param>
        /// <param name="hostStopping">Host shutdown token.</param>
        public void Start(ChannelReader<HistoryPersistItem> reader, CancellationToken hostStopping)
        {
            ArgumentNullException.ThrowIfNull(reader);
            if (Interlocked.CompareExchange(ref this._started, 1, 0) != 0)
            {
                return;
            }

            this._linkedCts = CancellationTokenSource.CreateLinkedTokenSource(hostStopping);
            CancellationToken token = this._linkedCts.Token;
            this._persistTask = Task.Run(() => this.RunPersistLoopAsync(reader, token), CancellationToken.None);
            this._logger.LogInformation(
                "HistoryDB Rocks persist pump started for {DbDir}",
                Path.GetFullPath(this._options.DbDir));
        }

        /// <summary>
        /// Drains the persist queue into RocksDB.
        /// </summary>
        /// <param name="reader">Persist queue reader.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private async Task RunPersistLoopAsync(ChannelReader<HistoryPersistItem> reader, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (HistoryPersistItem item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        this._rocks.PutReservation(item.Digest, item.ExpirationEpochSeconds);
                        this._metrics.RecordRocksPersist();
                        long count = Interlocked.Increment(ref this._totalPersisted);
                        if (count == 1)
                        {
                            this._logger.LogInformation(
                                "HistoryDB persist pump wrote first CHECK reservation to RocksDB at {DbDir}",
                                Path.GetFullPath(this._options.DbDir));
                        }
                        else if (count % 10_000 == 0)
                        {
                            this._logger.LogInformation(
                                "HistoryDB persist pump has written {Count} reservations to RocksDB",
                                count);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        this._logger.LogError(ex, "Failed to persist history entry to RocksDB");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this._logger.LogDebug("HistoryDB Rocks persist pump stopped");
            }
            catch (Exception ex)
            {
                this._logger.LogCritical(ex, "HistoryDB Rocks persist pump terminated unexpectedly");
                throw;
            }
        }
    }
}
