// <copyright file="HistoryMemoryCacheShardWriteResult.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Memory
{
    /// <summary>
    /// Aggregate deltas produced by a shard <see cref="HistoryMemoryCacheShard.InsertOrUpdate"/> or
    /// <see cref="HistoryMemoryCacheShard.Clear"/> for facade-level metrics and counters.
    /// </summary>
    internal readonly struct HistoryMemoryCacheShardWriteResult
    {
        /// <summary>
        /// Gets the net change in authoritative dictionary entries.
        /// </summary>
        internal int EntryDelta { get; init; }

        /// <summary>
        /// Gets the net change in tracked logical bytes.
        /// </summary>
        internal long ByteDelta { get; init; }

        /// <summary>
        /// Gets the net change in eviction heap size (includes tombstone pops).
        /// </summary>
        internal int HeapDelta { get; init; }

        /// <summary>
        /// Gets the number of dictionary entries removed by eviction.
        /// </summary>
        internal int EvictionCount { get; init; }

        /// <summary>
        /// Gets a value indicating whether the shard mutated state (false on no-op insert).
        /// </summary>
        internal bool Changed { get; init; }
    }
}
