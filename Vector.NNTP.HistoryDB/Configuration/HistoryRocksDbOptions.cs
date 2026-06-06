// <copyright file="HistoryRocksDbOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Configuration
{
    /// <summary>
    /// RocksDB tuning knobs (initial defaults subject to benchmark validation).
    /// </summary>
    /// <remarks>
    /// <para><b>Bloom filters:</b> <see cref="DigestBloomBitsPerKey"/> accelerates <c>by_digest</c> point lookups during
    /// Rocks persist. CHECK does not consult RocksDB; Bloom is a cold-path optimization for compaction and
    /// <c>PutReservation</c> existence checks.</para>
    /// </remarks>
    public sealed class HistoryRocksDbOptions
    {
        /// <summary>
        /// Default Bloom bits per key for the digest column family (~1% false-positive rate at 10 bits).
        /// </summary>
        public const int DefaultDigestBloomBitsPerKey = 10;

        /// <summary>
        /// Gets or sets the block cache size in bytes (0 = use implementation default).
        /// </summary>
        public long BlockCacheBytes { get; set; }

        /// <summary>
        /// Gets or sets the memtable write buffer size in bytes (0 = use implementation default).
        /// </summary>
        public long WriteBufferBytes { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of write buffers (0 = use implementation default).
        /// </summary>
        public int MaxWriteBufferNumber { get; set; }

        /// <summary>
        /// Gets or sets the target background compaction jobs passed to RocksDB <c>max_background_compactions</c> (0 = default).
        /// </summary>
        public int MaxBackgroundJobs { get; set; }

        /// <summary>
        /// Gets or sets Bloom filter bits per key for the <c>by_digest</c> column family (0 disables block Bloom).
        /// </summary>
        /// <remarks>
        /// Fixed 32-byte digest keys use whole-key Bloom checks. Ten bits per key is the RocksDB default trade-off
        /// between filter size and false-positive rate.
        /// </remarks>
        public int DigestBloomBitsPerKey { get; set; } = DefaultDigestBloomBitsPerKey;

        /// <summary>
        /// Gets or sets Bloom filter bits per key for the <c>by_expiration</c> column family (0 disables block Bloom).
        /// </summary>
        /// <remarks>
        /// Sweep and rebuild are iterator-heavy; expiration Bloom is optional and off by default.
        /// </remarks>
        public int ExpirationBloomBitsPerKey { get; set; }

        /// <summary>
        /// Gets or sets the SST block size in bytes (0 = RocksDB default, typically 4096).
        /// </summary>
        public int BlockSizeBytes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether index and filter blocks are stored in the block cache.
        /// </summary>
        public bool CacheIndexAndFilterBlocks { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether level-0 filter and index blocks are pinned in cache.
        /// </summary>
        public bool PinL0FilterAndIndexBlocksInCache { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether RocksDB should optimize Bloom filters for hit-heavy workloads on
        /// <c>by_digest</c>.
        /// </summary>
        public bool OptimizeDigestFiltersForHits { get; set; } = true;

        /// <summary>
        /// Gets or sets the memtable prefix Bloom ratio for <c>by_expiration</c> (0 disables).
        /// </summary>
        /// <remarks>
        /// Values above 0.25 are rarely beneficial; RocksDB clamps internally. Useful when expiration-prefix probes
        /// become common in future schema extensions.
        /// </remarks>
        public double ExpirationMemtablePrefixBloomRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether RocksDB collects internal statistics (required for meaningful periodic LOG dumps).
        /// </summary>
        /// <remarks>
        /// When <see langword="false"/>, <c>Options.statistics</c> stays null and only open-time LOG sections appear
        /// even if <see cref="StatsDumpPeriodSec"/> is non-zero.
        /// </remarks>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Gets or sets the interval in seconds for RocksDB stats snapshots (0 disables).
        /// </summary>
        /// <remarks>
        /// <para>Default 600 (10 minutes). Passed to RocksDB as <c>stats_dump_period_sec</c> and used by the host
        /// <c>HistoryRocksStatsLogHostedService</c> to log <c>rocksdb.stats</c> and ticker statistics.</para>
        /// <para>RocksDbSharp 6.2.x often does not emit periodic dumps into the RocksDB <c>LOG</c> file even when statistics
        /// are enabled; rely on host logs for scheduled snapshots.</para>
        /// </remarks>
        public uint StatsDumpPeriodSec { get; set; } = 600;
    }
}
