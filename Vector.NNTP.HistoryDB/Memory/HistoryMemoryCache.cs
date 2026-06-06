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
    /// <para>Eviction uses a min-heap with lazy tombstones: heap entries are not updated on expiration bump;
    /// stale tops are discarded during eviction by comparing against the authoritative dictionary.</para>
    /// </remarks>
    internal sealed class HistoryMemoryCache
    {
        /// <summary>
        /// The number of bytes per entry.
        /// </summary>
        private const int BytesPerEntry = HistoryKeyEncoder.DigestLength + 8;

        /// <summary>
        /// The limit bytes.
        /// </summary>
        private readonly long _limitBytes;

        /// <summary>
        /// Authoritative digest to expiration map.
        /// </summary>
        private readonly Dictionary<DigestKey, ulong> _entries = new();

        /// <summary>
        /// Expiration-ordered eviction candidates (may contain lazy tombstones).
        /// </summary>
        private readonly ExpirationMinHeap _evictionHeap = new();

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics;

        /// <summary>
        /// The tracked bytes.
        /// </summary>
        private long _trackedBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryMemoryCache"/> class.
        /// </summary>
        /// <param name="limitBytes">Maximum tracked bytes.</param>
        /// <param name="metrics">Metrics recorder.</param>
        internal HistoryMemoryCache(long limitBytes, HistoryMetrics metrics)
        {
            _limitBytes = limitBytes;
            _metrics = metrics;
        }

        /// <summary>
        /// Gets tracked entry count.
        /// </summary>
        internal int Count => _entries.Count;

        /// <summary>
        /// Tries a duplicate lookup without allocating (hot path).
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="nowEpochSeconds">Current UTC epoch seconds.</param>
        /// <returns><see langword="true"/> when an unexpired entry exists.</returns>
        internal bool TryGetDuplicate(in DigestKey digestKey, ulong nowEpochSeconds)
        {
            if (!_entries.TryGetValue(digestKey, out ulong expiration))
            {
                _metrics.RecordMemoryMiss();
                return false;
            }

            if (expiration <= nowEpochSeconds)
            {
                _metrics.RecordMemoryMiss();
                return false;
            }

            _metrics.RecordMemoryHit();
            return true;
        }

        /// <summary>
        /// Inserts or updates an entry and evicts oldest expirations when over budget.
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="expirationEpochSeconds">Expiration epoch.</param>
        internal void InsertOrUpdate(in DigestKey digestKey, ulong expirationEpochSeconds)
        {
            if (_entries.TryGetValue(digestKey, out ulong existing) && existing >= expirationEpochSeconds)
            {
                return;
            }

            bool added = !_entries.ContainsKey(digestKey);
            _entries[digestKey] = expirationEpochSeconds;
            _evictionHeap.Push(expirationEpochSeconds, in digestKey);
            if (added)
            {
                _trackedBytes += BytesPerEntry;
            }

            _metrics.SetMemoryEntries(_entries.Count);
            _metrics.SetMemoryBytes(_trackedBytes);
            EvictIfNeeded();
        }

        /// <summary>
        /// Clears all entries (used by tests).
        /// </summary>
        internal void Clear()
        {
            _entries.Clear();
            _evictionHeap.Clear();
            _trackedBytes = 0;
            _metrics.SetMemoryEntries(0);
            _metrics.SetMemoryBytes(0);
        }

        /// <summary>
        /// Evicts lowest-expiration entries until within the byte budget.
        /// </summary>
        private void EvictIfNeeded()
        {
            while (_trackedBytes > _limitBytes && _evictionHeap.TryPeek(out ulong heapExp, out DigestKey heapKey))
            {
                if (!_entries.TryGetValue(heapKey, out ulong currentExp) || currentExp != heapExp)
                {
                    _evictionHeap.Pop();
                    continue;
                }

                if (_entries.Remove(heapKey))
                {
                    _trackedBytes -= BytesPerEntry;
                    _metrics.RecordMemoryEviction();
                }

                _evictionHeap.Pop();
            }

            _metrics.SetMemoryEntries(_entries.Count);
            _metrics.SetMemoryBytes(_trackedBytes);
        }
    }
}
