// <copyright file="HistoryMemoryCache.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.HistoryDB.Memory
{
    /// <summary>
    /// Sharded bounded in-memory duplicate filter keyed by digest (newest / highest expiration retained).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as a process singleton and invoked concurrently from every transit session (CHECK and TAKETHIS).
    /// Digests map to one of <see cref="ShardCount"/> shards via <see cref="DigestKey.GetShardIndex"/> so unrelated
    /// keys avoid sharing a monitor. Each <see cref="HistoryMemoryCacheShard"/> owns a dictionary, min-heap, and
    /// <c>MemoryLimitBytes / ShardCount</c> logical byte budget.
    /// </para>
    /// <para>
    /// OpenTelemetry hit/miss counters run after the shard lock is released. Gauge aggregates use
    /// <see cref="Interlocked"/> deltas from shard writes instead of scanning all shards per insert.
    /// </para>
    /// <para>Eviction uses a min-heap with lazy tombstones per shard; observe
    /// <c>history.memory.heap_entries / history.memory.entries</c> for tombstone inflation.</para>
    /// <para>
    /// Expired entries are treated as misses on read and are not removed until shard memory-pressure eviction runs.
    /// </para>
    /// <para>
    /// Logical byte budgeting uses digest payload size (32-byte digest + 8-byte expiration), not actual managed heap
    /// consumption.
    /// </para>
    /// </remarks>
    internal sealed class HistoryMemoryCache
    {
        /// <summary>
        /// Logical payload bytes per entry used for budget enforcement (digest + expiration epoch).
        /// </summary>
        internal const int LogicalBytesPerEntry = HistoryKeyEncoder.DigestLength + 8;

        /// <summary>
        /// OpenTelemetry recorder for memory hit/miss counters and gauge deltas.
        /// </summary>
        private readonly HistoryMetrics _metrics;

        /// <summary>
        /// Shard mask (<c>shardCount - 1</c>).
        /// </summary>
        private readonly int _shardMask;

        /// <summary>
        /// Per-digest shards.
        /// </summary>
        private readonly HistoryMemoryCacheShard[] _shards;

        /// <summary>
        /// Authoritative entry count mirrored via <see cref="Interlocked"/> for lock-free reads.
        /// </summary>
        private int _entryCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryMemoryCache"/> class.
        /// </summary>
        /// <param name="limitBytes">Maximum tracked logical bytes across all shards.</param>
        /// <param name="shardCount">Number of shards (power of two).</param>
        /// <param name="metrics">Metrics recorder.</param>
        internal HistoryMemoryCache(long limitBytes, int shardCount, HistoryMetrics metrics)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(shardCount, 1);
            if ((shardCount & (shardCount - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(shardCount), shardCount, "Shard count must be a power of two.");
            }

            _metrics = metrics;
            ShardCount = shardCount;
            _shardMask = shardCount - 1;
            long shardLimitBytes = limitBytes / shardCount;
            _shards = new HistoryMemoryCacheShard[shardCount];
            for (int i = 0; i < shardCount; i++)
            {
                _shards[i] = new HistoryMemoryCacheShard(shardLimitBytes);
            }
        }

        /// <summary>
        /// Gets the configured shard count.
        /// </summary>
        internal int ShardCount { get; }

        /// <summary>
        /// Gets tracked entry count without acquiring a shard monitor.
        /// </summary>
        internal int Count => Volatile.Read(ref _entryCount);

        /// <summary>
        /// Gets the shard index for a digest (test and diagnostics).
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <returns>Shard index.</returns>
        internal int GetShardIndexForKey(in DigestKey digestKey)
        {
            return digestKey.GetShardIndex(_shardMask);
        }

        /// <summary>
        /// Tries a duplicate lookup without allocating (hot path).
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="nowEpochSeconds">Current UTC epoch seconds.</param>
        /// <returns><see langword="true"/> when an unexpired entry exists.</returns>
        internal bool TryGetDuplicate(in DigestKey digestKey, ulong nowEpochSeconds)
        {
            int shardIndex = digestKey.GetShardIndex(_shardMask);
            bool hit = _shards[shardIndex].TryGetDuplicate(in digestKey, nowEpochSeconds);
            if (hit)
            {
                _metrics.RecordMemoryHit();
            }
            else
            {
                _metrics.RecordMemoryMiss();
            }

            return hit;
        }

        /// <summary>
        /// Inserts or updates an entry and evicts oldest expirations when over budget.
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="expirationEpochSeconds">Expiration epoch.</param>
        internal void InsertOrUpdate(in DigestKey digestKey, ulong expirationEpochSeconds)
        {
            int shardIndex = digestKey.GetShardIndex(_shardMask);
            HistoryMemoryCacheShardWriteResult result = _shards[shardIndex].InsertOrUpdate(in digestKey, expirationEpochSeconds);
            ApplyWriteResult(in result);
        }

        /// <summary>
        /// Clears all entries (used by tests).
        /// </summary>
        /// <remarks>
        /// Resets every shard and zeroes global aggregates. Any future per-shard counters must be cleared in
        /// <see cref="HistoryMemoryCacheShard.Clear"/> and folded into <see cref="HistoryMemoryCacheShardWriteResult"/>.
        /// </remarks>
        internal void Clear()
        {
            HistoryMemoryCacheShardWriteResult total = default;
            for (int i = 0; i < _shards.Length; i++)
            {
                HistoryMemoryCacheShardWriteResult shardResult = _shards[i].Clear();
                total = CombineWriteResults(total, shardResult);
            }

            _entryCount = 0;
            if (total.Changed)
            {
                _metrics.AddMemoryEntriesDelta(total.EntryDelta);
                _metrics.AddMemoryBytesDelta(total.ByteDelta);
                _metrics.AddMemoryHeapEntriesDelta(total.HeapDelta);
            }
            else
            {
                _metrics.SetMemoryEntries(0);
                _metrics.SetMemoryBytes(0);
                _metrics.SetMemoryHeapEntries(0);
            }
        }

        /// <summary>
        /// Applies shard write deltas to global counters and metrics.
        /// </summary>
        /// <param name="result">Shard write result.</param>
        private void ApplyWriteResult(in HistoryMemoryCacheShardWriteResult result)
        {
            if (!result.Changed)
            {
                return;
            }

            if (result.EntryDelta != 0)
            {
                _ = Interlocked.Add(ref _entryCount, result.EntryDelta);
                _metrics.AddMemoryEntriesDelta(result.EntryDelta);
            }

            if (result.ByteDelta != 0)
            {
                _metrics.AddMemoryBytesDelta(result.ByteDelta);
            }

            if (result.HeapDelta != 0)
            {
                _metrics.AddMemoryHeapEntriesDelta(result.HeapDelta);
            }

            if (result.EvictionCount > 0)
            {
                _metrics.RecordMemoryEvictions(result.EvictionCount);
            }
        }

        /// <summary>
        /// Combines two shard write results for bulk clear aggregation.
        /// </summary>
        /// <param name="left">Accumulated result.</param>
        /// <param name="right">Shard result to add.</param>
        /// <returns>Combined deltas.</returns>
        private static HistoryMemoryCacheShardWriteResult CombineWriteResults(
            HistoryMemoryCacheShardWriteResult left,
            HistoryMemoryCacheShardWriteResult right)
        {
            return !right.Changed
                ? left
                : new HistoryMemoryCacheShardWriteResult
                {
                    EntryDelta = left.EntryDelta + right.EntryDelta,
                    ByteDelta = left.ByteDelta + right.ByteDelta,
                    HeapDelta = left.HeapDelta + right.HeapDelta,
                    EvictionCount = left.EvictionCount + right.EvictionCount,
                    Changed = left.Changed || right.Changed,
                };
        }
    }
}
