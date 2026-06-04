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
        /// The entries.
        /// </summary>
        private readonly Dictionary<DigestKey, ulong> _entries = new();

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
        public HistoryMemoryCache(long limitBytes, HistoryMetrics metrics)
        {
            this._limitBytes = limitBytes;
            this._metrics = metrics;
        }

        /// <summary>
        /// Gets tracked entry count.
        /// </summary>
        public int Count => this._entries.Count;

        /// <summary>
        /// Tries a duplicate lookup without allocating (hot path).
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="nowEpochSeconds">Current UTC epoch seconds.</param>
        /// <returns><see langword="true"/> when an unexpired entry exists.</returns>
        public bool TryGetDuplicate(in DigestKey digestKey, ulong nowEpochSeconds)
        {
            if (!this._entries.TryGetValue(digestKey, out ulong expiration))
            {
                this._metrics.RecordMemoryMiss();
                return false;
            }

            if (expiration <= nowEpochSeconds)
            {
                this._metrics.RecordMemoryMiss();
                return false;
            }

            this._metrics.RecordMemoryHit();
            return true;
        }

        /// <summary>
        /// Inserts or updates an entry and evicts oldest expirations when over budget.
        /// </summary>
        /// <param name="digestKey">Digest key.</param>
        /// <param name="expirationEpochSeconds">Expiration epoch.</param>
        public void InsertOrUpdate(in DigestKey digestKey, ulong expirationEpochSeconds)
        {
            if (this._entries.TryGetValue(digestKey, out ulong existing) && existing >= expirationEpochSeconds)
            {
                return;
            }

            bool added = !this._entries.ContainsKey(digestKey);
            this._entries[digestKey] = expirationEpochSeconds;
            if (added)
            {
                this._trackedBytes += BytesPerEntry;
            }

            this._metrics.SetMemoryEntries(this._entries.Count);
            this._metrics.SetMemoryBytes(this._trackedBytes);
            this.EvictIfNeeded();
        }

        /// <summary>
        /// Clears all entries (used by tests).
        /// </summary>
        public void Clear()
        {
            this._entries.Clear();
            this._trackedBytes = 0;
            this._metrics.SetMemoryEntries(0);
            this._metrics.SetMemoryBytes(0);
        }

        /// <summary>
        /// Evicts lowest-expiration entries until within the byte budget.
        /// </summary>
        private void EvictIfNeeded()
        {
            while (this._trackedBytes > this._limitBytes && this._entries.Count > 0)
            {
                DigestKey oldestKey = default;
                ulong oldestExp = ulong.MaxValue;
                foreach (KeyValuePair<DigestKey, ulong> pair in this._entries)
                {
                    if (pair.Value < oldestExp)
                    {
                        oldestExp = pair.Value;
                        oldestKey = pair.Key;
                    }
                }

                if (oldestExp == ulong.MaxValue)
                {
                    break;
                }

                if (this._entries.Remove(oldestKey))
                {
                    this._trackedBytes -= BytesPerEntry;
                    this._metrics.RecordMemoryEviction();
                }
            }

            this._metrics.SetMemoryEntries(this._entries.Count);
            this._metrics.SetMemoryBytes(this._trackedBytes);
        }
    }
}
