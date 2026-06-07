// <copyright file="HistoryMemoryCacheShard.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Encoding;

namespace Vector.NNTP.HistoryDB.Memory
{
    /// <summary>
    /// One shard of the in-memory history cache: dictionary, eviction heap, and per-shard byte budget.
    /// </summary>
    /// <remarks>
    /// <para>Callers route by digest shard index so unrelated digests avoid sharing a monitor.</para>
    /// <para>Does not touch <see cref="Metrics.HistoryMetrics"/>; returns deltas for the facade to aggregate.</para>
    /// </remarks>
    internal sealed class HistoryMemoryCacheShard
    {
        /// <summary>
        /// Logical payload bytes per entry used for budget enforcement (digest + expiration epoch).
        /// </summary>
        private const int LogicalBytesPerEntry = HistoryKeyEncoder.DigestLength + 8;

        /// <summary>
        /// Per-shard logical byte budget.
        /// </summary>
        private readonly long _limitBytes;

        /// <summary>
        /// Authoritative digest to expiration map.
        /// </summary>
        private readonly Dictionary<DigestKey, ulong> _entries = [];

        /// <summary>
        /// Expiration-ordered eviction candidates (may contain lazy tombstones).
        /// </summary>
        private readonly ExpirationMinHeap _evictionHeap = new();

        /// <summary>
        /// Exclusive gate for dictionary, heap, and budget fields.
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// Tracked logical bytes under <see cref="_syncRoot"/>.
        /// </summary>
        private long _trackedBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryMemoryCacheShard"/> class.
        /// </summary>
        /// <param name="limitBytes">Maximum tracked logical bytes for this shard.</param>
        /// <exception cref="OverflowException">Thrown when the tracked bytes exceed the limit.</exception>
        internal HistoryMemoryCacheShard(long limitBytes)
        {
            _limitBytes = limitBytes;
        }

        /// <summary>
        /// Gets tracked entry count for this shard (test and diagnostics).
        /// </summary>
        /// <exception cref="OverflowException">Thrown when the tracked bytes exceed the limit.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the eviction heap is empty.</exception>
        internal int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>
        /// Gets eviction heap size for this shard (test and diagnostics).
        /// </summary>
        internal int HeapCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _evictionHeap.Count;
                }
            }
        }

        /// <summary>
        /// Tries a duplicate lookup without allocating (hot path).
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="nowEpochSeconds">Current UTC epoch seconds.</param>
        /// <returns><see langword="true"/> when an unexpired entry exists.</returns>
        /// <exception cref="OverflowException">Thrown when the tracked bytes exceed the limit.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the eviction heap is empty.</exception>
        internal bool TryGetDuplicate(in DigestKey digestKey, ulong nowEpochSeconds)
        {
            lock (_syncRoot)
            {
                return _entries.TryGetValue(digestKey, out ulong expiration) && expiration > nowEpochSeconds;
            }
        }

        /// <summary>
        /// Inserts or updates an entry and evicts oldest expirations when over the shard budget.
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="expirationEpochSeconds">Expiration epoch.</param>
        /// <returns>Aggregate deltas for facade metrics.</returns>
        /// <exception cref="OverflowException">Thrown when the tracked bytes exceed the limit.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the eviction heap is empty.</exception>
        internal HistoryMemoryCacheShardWriteResult InsertOrUpdate(in DigestKey digestKey, ulong expirationEpochSeconds)
        {
            lock (_syncRoot)
            {
                int heapBefore = _evictionHeap.Count;
                int entryBefore = _entries.Count;
                long bytesBefore = _trackedBytes;

                bool exists = _entries.TryGetValue(digestKey, out ulong existing);
                if (exists && existing >= expirationEpochSeconds)
                {
                    return default;
                }

                if (!exists)
                {
                    _trackedBytes += LogicalBytesPerEntry;
                }

                _entries[digestKey] = expirationEpochSeconds;
                _evictionHeap.Push(expirationEpochSeconds, in digestKey);
                int evictions = EvictIfNeeded();

                return new HistoryMemoryCacheShardWriteResult
                {
                    EntryDelta = _entries.Count - entryBefore,
                    ByteDelta = _trackedBytes - bytesBefore,
                    HeapDelta = _evictionHeap.Count - heapBefore,
                    EvictionCount = evictions,
                    Changed = true,
                };
            }
        }

        /// <summary>
        /// Clears all entries in this shard.
        /// </summary>
        /// <returns>Deltas negating prior shard state for facade aggregates.</returns>
        /// <exception cref="OverflowException">Thrown when the tracked bytes exceed the limit.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the eviction heap is empty.</exception>
        internal HistoryMemoryCacheShardWriteResult Clear()
        {
            lock (_syncRoot)
            {
                int entryBefore = _entries.Count;
                long bytesBefore = _trackedBytes;
                int heapBefore = _evictionHeap.Count;
                if (entryBefore == 0 && bytesBefore == 0 && heapBefore == 0)
                {
                    return default;
                }

                _entries.Clear();
                _evictionHeap.Clear();
                _trackedBytes = 0;

                return new HistoryMemoryCacheShardWriteResult
                {
                    EntryDelta = -entryBefore,
                    ByteDelta = -bytesBefore,
                    HeapDelta = -heapBefore,
                    EvictionCount = 0,
                    Changed = true,
                };
            }
        }

        /// <summary>
        /// Evicts lowest-expiration entries until within the shard byte budget.
        /// </summary>
        /// <remarks>Caller must hold <see cref="_syncRoot"/>.</remarks>
        /// <returns>Number of dictionary entries removed.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the eviction heap is empty.</exception>
        /// <exception cref="OverflowException">Thrown when the tracked bytes exceed the limit.</exception>
        private int EvictIfNeeded()
        {
            int evictions = 0;
            while (_trackedBytes > _limitBytes && _evictionHeap.TryPeek(out ulong heapExp, out DigestKey heapKey))
            {
                if (!_entries.TryGetValue(heapKey, out ulong currentExp) || currentExp != heapExp)
                {
                    _evictionHeap.Pop();
                    continue;
                }

                if (_entries.Remove(heapKey))
                {
                    _trackedBytes -= LogicalBytesPerEntry;
                    evictions++;
                }

                _evictionHeap.Pop();
            }

            return evictions;
        }
    }
}
