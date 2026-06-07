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
        /// Net change in authoritative dictionary entries (negative when evictions or clear dominate).
        /// </summary>
        internal int EntryDelta { get; init; }

        /// <summary>
        /// Net change in tracked logical bytes under the shard budget.
        /// </summary>
        internal long ByteDelta { get; init; }

        /// <summary>
        /// Net change in eviction heap size, including lazy tombstone pops during eviction.
        /// </summary>
        internal int HeapDelta { get; init; }

        /// <summary>
        /// Dictionary entries removed by byte-budget eviction during the operation.
        /// </summary>
        internal int EvictionCount { get; init; }

        /// <summary>
        /// Whether the shard mutated state; <see langword="false"/> on no-op insert with unchanged expiration.
        /// </summary>
        internal bool Changed { get; init; }
    }
}
