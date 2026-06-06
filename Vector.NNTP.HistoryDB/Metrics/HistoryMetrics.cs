// <copyright file="HistoryMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: OpenTelemetry instruments for HistoryDB tier observability and maintenance operations.

using System.Diagnostics.Metrics;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.HistoryDB.Metrics
{
    /// <summary>
    /// OpenTelemetry-style metrics for HistoryDB CHECK tiers, record path, and Rocks maintenance.
    /// </summary>
    /// <remarks>
    /// <para><b>CHECK dashboard rates:</b></para>
    /// <list type="bullet">
    /// <item><c>memory_hit_rate = history.check.memory_hit / history.check.total</c></item>
    /// <item><c>redis_probe_rate = history.check.redis_probe / history.check.total</c></item>
    /// <item><c>redis_duplicate_rate = history.check.redis_duplicate / history.check.redis_probe</c></item>
    /// </list>
    /// <para><c>history.check.total</c> increments only on terminal Duplicate or Wanted outcomes (successfully processed CHECKs).</para>
    /// </remarks>
    internal sealed class HistoryMetrics
    {
        /// <summary>
        /// Shared metrics meter for the HistoryDB assembly.
        /// </summary>
        private static readonly Meter Meter = new("Vector.NNTP.HistoryDB", AssemblyInfoUtilities.ApplicationVersion);

        /// <summary>
        /// Successfully processed CHECK counter (Duplicate + Wanted terminal outcomes).
        /// </summary>
        private readonly Counter<long> _checkTotal;

        /// <summary>
        /// Memory-tier CHECK hit counter.
        /// </summary>
        private readonly Counter<long> _checkMemoryHit;

        /// <summary>
        /// Memory-tier CHECK miss counter.
        /// </summary>
        private readonly Counter<long> _checkMemoryMiss;

        /// <summary>
        /// Redis CHECK probe counter (memory miss path).
        /// </summary>
        private readonly Counter<long> _checkRedisProbe;

        /// <summary>
        /// Redis CHECK duplicate counter.
        /// </summary>
        private readonly Counter<long> _checkRedisDuplicate;

        /// <summary>
        /// Redis CHECK wanted counter.
        /// </summary>
        private readonly Counter<long> _checkRedisWanted;

        /// <summary>
        /// Terminal CHECK duplicate counter (memory or Redis).
        /// </summary>
        private readonly Counter<long> _checkDuplicate;

        /// <summary>
        /// Terminal CHECK wanted counter.
        /// </summary>
        private readonly Counter<long> _checkWanted;

        /// <summary>
        /// CHECK try-again counter.
        /// </summary>
        private readonly Counter<long> _checkTryAgain;

        /// <summary>
        /// CHECK unavailable counter.
        /// </summary>
        private readonly Counter<long> _checkUnavailable;

        /// <summary>
        /// Record success counter.
        /// </summary>
        private readonly Counter<long> _recordRecorded;

        /// <summary>
        /// Record duplicate counter.
        /// </summary>
        private readonly Counter<long> _recordDuplicate;

        /// <summary>
        /// Record try-again counter.
        /// </summary>
        private readonly Counter<long> _recordTryAgain;

        /// <summary>
        /// Record unavailable counter.
        /// </summary>
        private readonly Counter<long> _recordUnavailable;

        /// <summary>
        /// Persist queue drop counter.
        /// </summary>
        private readonly Counter<long> _queueDropped;

        /// <summary>
        /// Memory eviction counter.
        /// </summary>
        private readonly Counter<long> _memoryEvictions;

        /// <summary>
        /// Rocks persist success counter.
        /// </summary>
        private readonly Counter<long> _rocksPersistTotal;

        /// <summary>
        /// Rocks persist failure counter.
        /// </summary>
        private readonly Counter<long> _rocksPersistFailures;

        /// <summary>
        /// Rocks sweep deleted-keys counter.
        /// </summary>
        private readonly Counter<long> _rocksSweepDeleted;

        /// <summary>
        /// Generation file I/O error counter.
        /// </summary>
        private readonly Counter<long> _generationIoErrors;

        /// <summary>
        /// Slow Redis Lua call counter.
        /// </summary>
        private readonly Counter<long> _redisSlowCalls;

        /// <summary>
        /// CHECK Redis latency histogram.
        /// </summary>
        private readonly Histogram<double> _redisMs;

        /// <summary>
        /// Record Redis latency histogram.
        /// </summary>
        private readonly Histogram<double> _recordRedisMs;

        /// <summary>
        /// Rebuild batch latency histogram.
        /// </summary>
        private readonly Histogram<double> _rebuildBatchMs;

        /// <summary>
        /// Full rebuild duration histogram.
        /// </summary>
        private readonly Histogram<double> _rebuildDurationMs;

        /// <summary>
        /// Sweep duration histogram.
        /// </summary>
        private readonly Histogram<double> _sweepMs;

        /// <summary>
        /// Preload duration histogram.
        /// </summary>
        private readonly Histogram<double> _preloadMs;

        /// <summary>
        /// Memory entry count gauge backing field.
        /// </summary>
        private long _memoryEntries;

        /// <summary>
        /// Memory byte gauge backing field.
        /// </summary>
        private long _memoryBytes;

        /// <summary>
        /// Rebuild keys processed gauge backing field.
        /// </summary>
        private long _rebuildKeysProcessed;

        /// <summary>
        /// Operational gate gauge backing field (0 or 1).
        /// </summary>
        private long _operational;

        /// <summary>
        /// Persist queue depth gauge backing field.
        /// </summary>
        private long _queueDepth;

        /// <summary>
        /// Initializes metric instruments for the HistoryDB assembly.
        /// </summary>
        internal HistoryMetrics()
        {
            _checkTotal = Meter.CreateCounter<long>("history.check.total");
            _checkMemoryHit = Meter.CreateCounter<long>("history.check.memory_hit");
            _checkMemoryMiss = Meter.CreateCounter<long>("history.check.memory_miss");
            _checkRedisProbe = Meter.CreateCounter<long>("history.check.redis_probe");
            _checkRedisDuplicate = Meter.CreateCounter<long>("history.check.redis_duplicate");
            _checkRedisWanted = Meter.CreateCounter<long>("history.check.redis_wanted");
            _checkDuplicate = Meter.CreateCounter<long>("history.check.duplicate");
            _checkWanted = Meter.CreateCounter<long>("history.check.wanted");
            _checkTryAgain = Meter.CreateCounter<long>("history.check.try_again");
            _checkUnavailable = Meter.CreateCounter<long>("history.check.unavailable");
            _recordRecorded = Meter.CreateCounter<long>("history.record.recorded");
            _recordDuplicate = Meter.CreateCounter<long>("history.record.duplicate");
            _recordTryAgain = Meter.CreateCounter<long>("history.record.try_again");
            _recordUnavailable = Meter.CreateCounter<long>("history.record.unavailable");
            _queueDropped = Meter.CreateCounter<long>("history.queue.dropped");
            _memoryEvictions = Meter.CreateCounter<long>("history.memory.evictions");
            _rocksPersistTotal = Meter.CreateCounter<long>("history.rocks.persist.total");
            _rocksPersistFailures = Meter.CreateCounter<long>("history.rocks.persist_failures");
            _rocksSweepDeleted = Meter.CreateCounter<long>("history.rocks.sweep.deleted");
            _generationIoErrors = Meter.CreateCounter<long>("history.generation.io_errors");
            _redisSlowCalls = Meter.CreateCounter<long>("history.redis.slow_calls");
            _redisMs = Meter.CreateHistogram<double>("history.check.redis_ms");
            _recordRedisMs = Meter.CreateHistogram<double>("history.record.redis_ms");
            _rebuildBatchMs = Meter.CreateHistogram<double>("history.rebuild.batch.duration_ms");
            _rebuildDurationMs = Meter.CreateHistogram<double>("history.rebuild.duration_ms");
            _sweepMs = Meter.CreateHistogram<double>("history.rocks.sweep.duration_ms");
            _preloadMs = Meter.CreateHistogram<double>("history.preload.duration_ms");

            _ = Meter.CreateObservableGauge(
                "history.memory.entries",
                () => new Measurement<long>(_memoryEntries),
                description: "Live in-memory history entries.");
            _ = Meter.CreateObservableGauge(
                "history.memory.bytes",
                () => new Measurement<long>(_memoryBytes),
                description: "Tracked in-memory history bytes.");
            _ = Meter.CreateObservableGauge(
                "history.rebuild.keys_processed",
                () => new Measurement<long>(_rebuildKeysProcessed),
                description: "Keys processed in the current or last rebuild.");
            _ = Meter.CreateObservableGauge(
                "history.operational",
                () => new Measurement<long>(_operational),
                description: "1 when CHECK and record paths are operational.");
            _ = Meter.CreateObservableGauge(
                "history.queue.depth",
                () => new Measurement<long>(_queueDepth),
                description: "Pending Rocks persist queue depth.");
        }

        /// <summary>
        /// Records a successfully processed CHECK (terminal Duplicate or Wanted).
        /// </summary>
        internal void RecordCheckTotal()
        {
            _checkTotal.Add(1);
        }

        /// <summary>
        /// Records a memory-tier CHECK hit.
        /// </summary>
        internal void RecordMemoryHit()
        {
            _checkMemoryHit.Add(1);
        }

        /// <summary>
        /// Records a memory-tier CHECK miss.
        /// </summary>
        internal void RecordMemoryMiss()
        {
            _checkMemoryMiss.Add(1);
        }

        /// <summary>
        /// Records a Redis CHECK probe invocation.
        /// </summary>
        internal void RecordRedisProbe()
        {
            _checkRedisProbe.Add(1);
        }

        /// <summary>
        /// Records a Redis CHECK duplicate outcome.
        /// </summary>
        internal void RecordRedisDuplicate()
        {
            _checkRedisDuplicate.Add(1);
        }

        /// <summary>
        /// Records a Redis CHECK wanted outcome.
        /// </summary>
        internal void RecordRedisWanted()
        {
            _checkRedisWanted.Add(1);
        }

        /// <summary>
        /// Records a terminal CHECK duplicate outcome.
        /// </summary>
        internal void RecordCheckDuplicate()
        {
            _checkDuplicate.Add(1);
        }

        /// <summary>
        /// Records a terminal CHECK wanted outcome.
        /// </summary>
        internal void RecordWanted()
        {
            _checkWanted.Add(1);
            RecordCheckTotal();
        }

        /// <summary>
        /// Records a terminal CHECK duplicate outcome and total.
        /// </summary>
        internal void RecordDuplicate()
        {
            _checkDuplicate.Add(1);
            RecordCheckTotal();
        }

        /// <summary>
        /// Records a CHECK try-again outcome.
        /// </summary>
        internal void RecordTryAgain()
        {
            _checkTryAgain.Add(1);
        }

        /// <summary>
        /// Records a CHECK unavailable outcome.
        /// </summary>
        internal void RecordUnavailable()
        {
            _checkUnavailable.Add(1);
        }

        /// <summary>
        /// Records a successful history record on accept path.
        /// </summary>
        internal void RecordRecorded()
        {
            _recordRecorded.Add(1);
        }

        /// <summary>
        /// Records duplicate on record path.
        /// </summary>
        internal void RecordRecordDuplicate()
        {
            _recordDuplicate.Add(1);
        }

        /// <summary>
        /// Records try-again on record path.
        /// </summary>
        internal void RecordRecordTryAgain()
        {
            _recordTryAgain.Add(1);
        }

        /// <summary>
        /// Records unavailable on record path.
        /// </summary>
        internal void RecordRecordUnavailable()
        {
            _recordUnavailable.Add(1);
        }

        /// <summary>
        /// Records queue drop after Redis record.
        /// </summary>
        internal void RecordQueueDropped()
        {
            _queueDropped.Add(1);
        }

        /// <summary>
        /// Records memory eviction.
        /// </summary>
        internal void RecordMemoryEviction()
        {
            _memoryEvictions.Add(1);
        }

        /// <summary>
        /// Records a successful RocksDB persist from the background queue.
        /// </summary>
        internal void RecordRocksPersist()
        {
            _rocksPersistTotal.Add(1);
        }

        /// <summary>
        /// Records a RocksDB persist failure.
        /// </summary>
        internal void RecordPersistFailure()
        {
            _rocksPersistFailures.Add(1);
        }

        /// <summary>
        /// Records keys deleted by a sweep pass.
        /// </summary>
        /// <param name="deleted">Keys removed.</param>
        internal void RecordSweepDeleted(int deleted)
        {
            if (deleted > 0)
            {
                _rocksSweepDeleted.Add(deleted);
            }
        }

        /// <summary>
        /// Records a generation file I/O error.
        /// </summary>
        internal void RecordGenerationIoError()
        {
            _generationIoErrors.Add(1);
        }

        /// <summary>
        /// Records a slow Redis Lua invocation.
        /// </summary>
        internal void RecordRedisSlowCall()
        {
            _redisSlowCalls.Add(1);
        }

        /// <summary>
        /// Records Redis Lua duration for CHECK probe.
        /// </summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        internal void RecordRedisMilliseconds(double milliseconds)
        {
            _redisMs.Record(milliseconds);
        }

        /// <summary>
        /// Records Redis Lua duration for record.
        /// </summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        internal void RecordRecordRedisMilliseconds(double milliseconds)
        {
            _recordRedisMs.Record(milliseconds);
        }

        /// <summary>
        /// Records rebuild batch duration.
        /// </summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        internal void RecordRebuildBatchMilliseconds(double milliseconds)
        {
            _rebuildBatchMs.Record(milliseconds);
        }

        /// <summary>
        /// Records full rebuild wall-clock duration.
        /// </summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        internal void RecordRebuildDurationMilliseconds(double milliseconds)
        {
            _rebuildDurationMs.Record(milliseconds);
        }

        /// <summary>
        /// Records sweep duration.
        /// </summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        internal void RecordSweepMilliseconds(double milliseconds)
        {
            _sweepMs.Record(milliseconds);
        }

        /// <summary>
        /// Records preload duration.
        /// </summary>
        /// <param name="milliseconds">Elapsed milliseconds.</param>
        internal void RecordPreloadMilliseconds(double milliseconds)
        {
            _preloadMs.Record(milliseconds);
        }

        /// <summary>
        /// Sets memory entry gauge.
        /// </summary>
        /// <param name="count">Entry count.</param>
        internal void SetMemoryEntries(int count)
        {
            _memoryEntries = count;
        }

        /// <summary>
        /// Sets memory bytes gauge.
        /// </summary>
        /// <param name="bytes">Tracked bytes.</param>
        internal void SetMemoryBytes(long bytes)
        {
            _memoryBytes = bytes;
        }

        /// <summary>
        /// Sets rebuild keys processed gauge.
        /// </summary>
        /// <param name="keys">Keys processed.</param>
        internal void SetRebuildKeysProcessed(long keys)
        {
            _rebuildKeysProcessed = keys;
        }

        /// <summary>
        /// Sets operational gate gauge.
        /// </summary>
        /// <param name="operational">1 when operational; otherwise 0.</param>
        internal void SetOperational(bool operational)
        {
            _operational = operational ? 1 : 0;
        }

        /// <summary>
        /// Sets persist queue depth gauge.
        /// </summary>
        /// <param name="depth">Pending items.</param>
        internal void SetQueueDepth(long depth)
        {
            _queueDepth = depth;
        }
    }
}
