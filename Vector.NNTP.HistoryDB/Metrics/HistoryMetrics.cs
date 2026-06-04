// <copyright file="HistoryMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics.Metrics;

namespace Vector.NNTP.HistoryDB.Metrics
{
    /// <summary>
    /// OpenTelemetry-style metrics for HistoryDB.
    /// </summary>
    internal sealed class HistoryMetrics
    {

        /// <summary>
        /// The metrics meter.
        /// </summary>
        private static readonly Meter Meter = new("Vector.NNTP.HistoryDB", "1.0.0");

        /// <summary>
        /// The check wanted counter.
        /// </summary>
        private readonly Counter<long> _checkWanted;

        /// <summary>
        /// The check try again counter.
        /// </summary>
        private readonly Counter<long> _checkTryAgain;

        /// <summary>
        /// The check unavailable counter.
        /// </summary>
        private readonly Counter<long> _checkUnavailable;

        /// <summary>
        /// The record recorded counter.
        /// </summary>
        private readonly Counter<long> _recordRecorded;

        /// <summary>
        /// The record try again counter.
        /// </summary>
        private readonly Counter<long> _recordTryAgain;

        /// <summary>
        /// The record unavailable counter.
        /// </summary>
        private readonly Counter<long> _recordUnavailable;

        /// <summary>
        /// The queue dropped counter.
        /// </summary>
        private readonly Counter<long> _queueDropped;

        /// <summary>
        /// The memory evictions counter.
        /// </summary>
        private readonly Counter<long> _memoryEvictions;

        /// <summary>
        /// The Redis milliseconds histogram.
        /// </summary>
        private readonly Histogram<double> _redisMs;

        /// <summary>
        /// The record Redis milliseconds histogram.
        /// </summary>
        private readonly Histogram<double> _recordRedisMs;

        /// <summary>
        /// The rebuild batch milliseconds histogram.
        /// </summary>
        private readonly Histogram<double> _rebuildBatchMs;

        /// <summary>
        /// The sweep milliseconds histogram.
        /// </summary>
        private readonly Histogram<double> _sweepMs;

        /// <summary>
        /// The preload milliseconds histogram.
        /// </summary>
        private readonly Histogram<double> _preloadMs;

        /// <summary>
        /// The memory entries gauge.
        /// </summary>
        private long _memoryEntries;

        /// <summary>
        /// The memory bytes gauge.
        /// </summary>
        private long _memoryBytes;

        /// <summary>
        /// The rebuild keys processed gauge.
        /// </summary>
        private long _rebuildKeysProcessed;

        /// <summary>
        /// The memory hits gauge.
        /// </summary>
        private long _memoryHits;

        /// <summary>
        /// The memory misses gauge.
        /// </summary>
        private long _memoryMisses;

        /// <summary>
        /// The check duplicates gauge.
        /// </summary>
        private long _checkDuplicates;

        /// <summary>
        /// The record duplicates gauge.
        /// </summary>
        private long _recordDuplicates;

        /// <summary>
        /// The Rocks persists gauge.
        /// </summary>
        private long _rocksPersists;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryMetrics"/> class.
        /// </summary>
        public HistoryMetrics()
        {
            this._checkWanted = Meter.CreateCounter<long>("history.check.wanted");
            this._checkTryAgain = Meter.CreateCounter<long>("history.check.try_again");
            this._checkUnavailable = Meter.CreateCounter<long>("history.check.unavailable");
            this._recordRecorded = Meter.CreateCounter<long>("history.record.recorded");
            this._recordTryAgain = Meter.CreateCounter<long>("history.record.try_again");
            this._recordUnavailable = Meter.CreateCounter<long>("history.record.unavailable");
            this._queueDropped = Meter.CreateCounter<long>("history.queue.dropped");
            this._memoryEvictions = Meter.CreateCounter<long>("history.memory.evictions");
            this._redisMs = Meter.CreateHistogram<double>("history.check.redis_ms");
            this._recordRedisMs = Meter.CreateHistogram<double>("history.record.redis_ms");
            this._rebuildBatchMs = Meter.CreateHistogram<double>("history.rebuild.batch.duration_ms");
            this._sweepMs = Meter.CreateHistogram<double>("history.rocks.sweep.duration_ms");
            this._preloadMs = Meter.CreateHistogram<double>("history.preload.duration_ms");
        }

        /// <summary>Records wanted CHECK outcome.</summary>
        public void RecordWanted() => this._checkWanted.Add(1);

        /// <summary>Records duplicate CHECK outcome (allocation-free hot path).</summary>
        public void RecordDuplicate() => Interlocked.Increment(ref this._checkDuplicates);

        /// <summary>Records try-again CHECK outcome.</summary>
        public void RecordTryAgain() => this._checkTryAgain.Add(1);

        /// <summary>Records unavailable CHECK outcome.</summary>
        public void RecordUnavailable() => this._checkUnavailable.Add(1);

        /// <summary>Records successful history record on accept path.</summary>
        public void RecordRecorded() => this._recordRecorded.Add(1);

        /// <summary>Records duplicate on record path.</summary>
        public void RecordRecordDuplicate() => Interlocked.Increment(ref this._recordDuplicates);

        /// <summary>Records try-again on record path.</summary>
        public void RecordRecordTryAgain() => this._recordTryAgain.Add(1);

        /// <summary>Records unavailable on record path.</summary>
        public void RecordRecordUnavailable() => this._recordUnavailable.Add(1);

        /// <summary>Records queue drop after Redis record.</summary>
        public void RecordQueueDropped() => this._queueDropped.Add(1);

        /// <summary>Records memory eviction.</summary>
        public void RecordMemoryEviction() => this._memoryEvictions.Add(1);

        /// <summary>Records a successful RocksDB persist from the background queue.</summary>
        public void RecordRocksPersist() => Interlocked.Increment(ref this._rocksPersists);

        /// <summary>Records memory hit on CHECK (allocation-free hot path).</summary>
        public void RecordMemoryHit() => Interlocked.Increment(ref this._memoryHits);

        /// <summary>Records memory miss on CHECK (allocation-free hot path).</summary>
        public void RecordMemoryMiss() => Interlocked.Increment(ref this._memoryMisses);

        /// <summary>Records Redis Lua duration for CHECK probe.</summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        public void RecordRedisMilliseconds(double milliseconds) => this._redisMs.Record(milliseconds);

        /// <summary>Records Redis Lua duration for record.</summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        public void RecordRecordRedisMilliseconds(double milliseconds) => this._recordRedisMs.Record(milliseconds);

        /// <summary>Records rebuild batch duration.</summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        public void RecordRebuildBatchMilliseconds(double milliseconds) => this._rebuildBatchMs.Record(milliseconds);

        /// <summary>Records sweep duration.</summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        public void RecordSweepMilliseconds(double milliseconds) => this._sweepMs.Record(milliseconds);

        /// <summary>Records preload duration.</summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        public void RecordPreloadMilliseconds(double milliseconds) => this._preloadMs.Record(milliseconds);

        /// <summary>Sets memory entry gauge.</summary>
        /// <param name="count">Entry count.</param>
        public void SetMemoryEntries(int count) => this._memoryEntries = count;

        /// <summary>Sets memory bytes gauge.</summary>
        /// <param name="bytes">Tracked bytes.</param>
        public void SetMemoryBytes(long bytes) => this._memoryBytes = bytes;

        /// <summary>Sets rebuild keys processed gauge.</summary>
        /// <param name="keys">Keys processed.</param>
        public void SetRebuildKeysProcessed(long keys) => this._rebuildKeysProcessed = keys;
    }
}
