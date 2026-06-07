// <copyright file="HistoryMemoryCache.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.HistoryDB.Memory
{
    /// <summary>
    /// Bounded in-memory duplicate filter keyed by digest (newest / highest expiration retained).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as a process singleton and invoked concurrently from every transit session (CHECK and TAKETHIS).
    /// A single <see cref="object"/> monitor protects the dictionary and eviction heap, so every CHECK and TAKETHIS
    /// serializes through one critical section. The section is a single <see cref="Dictionary{TKey, TValue}.TryGetValue"/>
    /// on the hot path and is expected to be cheap, but aggregate throughput is fundamentally single-lane until
    /// measured. Do not change synchronization strategy without BenchmarkDotNet evidence on production-like hardware.
    /// </para>
    /// <para>
    /// If contention appears on large core-count hosts (for example 48-core transit nodes), prefer digest-key sharding
    /// (for example 64 shards, each with its own dictionary, min-heap, and lock) over returning to
    /// <see cref="ReaderWriterLockSlim"/>: unrelated digests then avoid sharing a monitor.
    /// </para>
    /// <para>
    /// OpenTelemetry gauge updates and hit/miss counters run <b>after</b> the monitor is released so lock hold time
    /// stays minimal on the hot CHECK path.
    /// </para>
    /// <para>Eviction uses a min-heap with lazy tombstones: heap entries are not updated on expiration bump;
    /// stale tops are discarded during eviction by comparing against the authoritative dictionary. Repeated expiration
    /// bumps can inflate <c>history.memory.heap_entries</c> relative to <c>history.memory.entries</c>; observe the
    /// ratio to decide whether heap rebuild is warranted.</para>
    /// <para>
    /// Expired entries are treated as misses on read and are not removed until memory-pressure eviction runs. This
    /// defers mutation cost on the hot path but allows expired keys to remain resident while under the logical byte
    /// budget.
    /// </para>
    /// <para>
    /// <see cref="LogicalBytesPerEntry"/> budgets digest payload size (32-byte digest + 8-byte expiration), not actual
    /// managed heap consumption (dictionary nodes, heap storage, alignment).
    /// </para>
    /// </remarks>
    internal sealed class HistoryMemoryCache
    {
        /// <summary>
        /// Logical payload bytes per entry used for budget enforcement (digest + expiration epoch).
        /// </summary>
        private const int LogicalBytesPerEntry = HistoryKeyEncoder.DigestLength + 8;

        /// <summary>
        /// The limit bytes.
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
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics;

        /// <summary>
        /// Exclusive gate for dictionary, heap, and budget fields.
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// Authoritative entry count mirrored under <see cref="_syncRoot"/> for lock-free reads.
        /// </summary>
        private int _entryCount;

        /// <summary>
        /// The tracked bytes.
        /// </summary>
        private long _trackedBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryMemoryCache"/> class.
        /// </summary>
        /// <param name="limitBytes">Maximum tracked logical bytes.</param>
        /// <param name="metrics">Metrics recorder.</param>
        internal HistoryMemoryCache(long limitBytes, HistoryMetrics metrics)
        {
            _limitBytes = limitBytes;
            _metrics = metrics;
        }

        /// <summary>
        /// Gets tracked entry count without acquiring the monitor.
        /// </summary>
        internal int Count => Volatile.Read(ref _entryCount);

        /// <summary>
        /// Tries a duplicate lookup without allocating (hot path).
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="nowEpochSeconds">Current UTC epoch seconds.</param>
        /// <returns><see langword="true"/> when an unexpired entry exists.</returns>
        internal bool TryGetDuplicate(in DigestKey digestKey, ulong nowEpochSeconds)
        {
            bool hit;
            lock (_syncRoot)
            {
                hit = _entries.TryGetValue(digestKey, out ulong expiration) && expiration > nowEpochSeconds;
            }

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
            int entryCount;
            long trackedBytes;
            int heapCount;
            int evictions;
            lock (_syncRoot)
            {
                bool exists = _entries.TryGetValue(digestKey, out ulong existing);
                if (exists && existing >= expirationEpochSeconds)
                {
                    return;
                }

                if (!exists)
                {
                    _trackedBytes += LogicalBytesPerEntry;
                }

                _entries[digestKey] = expirationEpochSeconds;
                _evictionHeap.Push(expirationEpochSeconds, in digestKey);
                evictions = EvictIfNeeded();
                entryCount = _entries.Count;
                _entryCount = entryCount;
                trackedBytes = _trackedBytes;
                heapCount = _evictionHeap.Count;
            }

            if (evictions > 0)
            {
                _metrics.RecordMemoryEvictions(evictions);
            }

            _metrics.SetMemoryEntries(entryCount);
            _metrics.SetMemoryBytes(trackedBytes);
            _metrics.SetMemoryHeapEntries(heapCount);
        }

        /// <summary>
        /// Clears all entries (used by tests).
        /// </summary>
        /// <remarks>
        /// Resets every mutable cache field under <see cref="_syncRoot"/> (<see cref="_entries"/>,
        /// <see cref="_evictionHeap"/>, <see cref="_trackedBytes"/>, <see cref="_entryCount"/>). Any future per-cache
        /// counters (for example tombstone totals) must be zeroed here as well.
        /// </remarks>
        internal void Clear()
        {
            lock (_syncRoot)
            {
                _entries.Clear();
                _evictionHeap.Clear();
                _trackedBytes = 0;
                _entryCount = 0;
            }

            _metrics.SetMemoryEntries(0);
            _metrics.SetMemoryBytes(0);
            _metrics.SetMemoryHeapEntries(0);
        }

        /// <summary>
        /// Evicts lowest-expiration entries until within the byte budget.
        /// </summary>
        /// <remarks>Caller must hold <see cref="_syncRoot"/>.</remarks>
        /// <returns>Number of entries removed.</returns>
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
