// <copyright file="HistoryRocksDbOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Configuration;

namespace Vector.NNTP.HistoryDB.Configuration
{
    /// <summary>
    /// RocksDB tuning knobs (initial defaults subject to benchmark validation).
    /// </summary>
    /// <remarks>
    /// <para><b>Bloom filters:</b> <see cref="DigestBloomBitsPerKey"/> accelerates <c>by_digest</c> point lookups during
    /// Rocks persist. CHECK does not consult RocksDB; Bloom is a cold-path optimization for compaction and
    /// <c>PutReservation</c> existence checks.</para>
    /// <para><b>Block caches:</b> <see cref="DigestBlockCacheBytes"/> and <see cref="ExpirationBlockCacheBytes"/> are
    /// independent LRU caches per column family. Digest lookups are hot; expiration sweep/rebuild is maintenance-heavy.</para>
    /// </remarks>
    internal sealed class HistoryRocksDbOptions
    {
        /// <summary>
        /// Default Bloom bits per key for the digest column family (~1% false-positive rate at 10 bits).
        /// </summary>
        public const int DefaultDigestBloomBitsPerKey = 10;

        /// <summary>
        /// Default block cache size for the expiration column family (8 MB).
        /// </summary>
        public const long DefaultExpirationBlockCacheBytes = 8 * 1024 * 1024;

        /// <summary>
        /// <c>by_digest</c> LRU block cache size in bytes (0 = RocksDB default).
        /// </summary>
        /// <remarks>
        /// JSON key <c>BlockCacheBytes</c> is accepted for backward compatibility via
        /// <see cref="ConfigurationKeyNameAttribute"/>.
        /// </remarks>
        [ConfigurationKeyName("BlockCacheBytes")]
        public long DigestBlockCacheBytes { get; set; }

        /// <summary>
        /// <c>by_expiration</c> LRU block cache size in bytes (0 = RocksDB default).
        /// </summary>
        /// <remarks>Sweep and rebuild iterate this column family; default 8 MB keeps maintenance I/O bounded.</remarks>
        public long ExpirationBlockCacheBytes { get; set; } = DefaultExpirationBlockCacheBytes;

        /// <summary>
        /// Memtable write buffer size in bytes per column family (0 = implementation default).
        /// </summary>
        public long WriteBufferBytes { get; set; }

        /// <summary>
        /// Maximum number of memtable write buffers (0 = implementation default).
        /// </summary>
        public int MaxWriteBufferNumber { get; set; }

        /// <summary>
        /// Target background compaction thread count (0 = RocksDB default).
        /// </summary>
        /// <remarks>
        /// Passed to <c>SetMaxBackgroundCompactions</c> on open. The RocksDB 10.4.x C# bindings do not yet expose
        /// <c>max_background_jobs</c> as a single setter; this knob preserves the production mapping from the 6.2.2 host.
        /// </remarks>
        public int MaxBackgroundJobs { get; set; }

        /// <summary>
        /// Bloom filter bits per key for <c>by_digest</c> (0 disables block Bloom).
        /// </summary>
        /// <remarks>
        /// Fixed 32-byte digest keys use whole-key Bloom checks. Ten bits per key is the RocksDB default trade-off
        /// between filter size and false-positive rate.
        /// </remarks>
        public int DigestBloomBitsPerKey { get; set; } = DefaultDigestBloomBitsPerKey;

        /// <summary>
        /// Bloom filter bits per key for <c>by_expiration</c> (0 disables block Bloom).
        /// </summary>
        /// <remarks>
        /// Sweep and rebuild are iterator-heavy; expiration Bloom is optional and off by default.
        /// </remarks>
        public int ExpirationBloomBitsPerKey { get; set; }

        /// <summary>
        /// SST data block size in bytes (0 = RocksDB default, typically 4096).
        /// </summary>
        public int BlockSizeBytes { get; set; }

        /// <summary>
        /// When true, index and filter blocks are cached alongside data blocks.
        /// </summary>
        public bool CacheIndexAndFilterBlocks { get; set; } = true;

        /// <summary>
        /// When true, level-0 filter and index blocks stay pinned in the block cache.
        /// </summary>
        public bool PinL0FilterAndIndexBlocksInCache { get; set; } = true;

        /// <summary>
        /// When true, RocksDB optimizes <c>by_digest</c> Bloom filters for hit-heavy point lookups.
        /// </summary>
        public bool OptimizeDigestFiltersForHits { get; set; } = true;

        /// <summary>
        /// Memtable prefix Bloom ratio for <c>by_expiration</c> (0 disables).
        /// </summary>
        /// <remarks>
        /// Values above 0.25 are rarely beneficial; RocksDB clamps internally. Useful when expiration-prefix probes
        /// become common in future schema extensions.
        /// </remarks>
        public double ExpirationMemtablePrefixBloomRatio { get; set; }

        /// <summary>
        /// When true, RocksDB collects internal statistics required for periodic LOG dumps.
        /// </summary>
        /// <remarks>
        /// When <see langword="false"/>, <c>Options.statistics</c> stays null and periodic
        /// <c>DUMPING/PERSISTING STATS</c> sections are not written to <c>DbDir/LOG</c> even if
        /// <see cref="StatsDumpPeriodSec"/> is non-zero.
        /// </remarks>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Interval in seconds for RocksDB stats snapshots written to <c>DbDir/LOG</c> (0 disables).
        /// </summary>
        /// <remarks>
        /// <para>Default 600 (10 minutes). Passed to RocksDB as <c>stats_dump_period_sec</c>. On RocksDB 10.x with
        /// <see cref="EnableStatistics"/> enabled, the native runtime writes <c>------- DUMPING STATS -------</c> and
        /// <c>------- PERSISTING STATS -------</c> sections to <c>DbDir/LOG</c> on this interval (reliable on 10.x;
        /// the prior 6.2.2 bindings did not always emit periodic dumps).</para>
        /// <para>When <see cref="MirrorStatsToHostLogger"/> is <see langword="true"/>, the same interval drives
        /// <c>HistoryRocksStatsLogHostedService</c> to copy <c>rocksdb.stats</c> and ticker text into the NNTPD host
        /// logger.</para>
        /// </remarks>
        public uint StatsDumpPeriodSec { get; set; } = 600;

        /// <summary>
        /// When true, periodic RocksDB statistics are mirrored into the NNTPD host logger.
        /// </summary>
        /// <remarks>
        /// <para>Default <see langword="false"/>. RocksDB 10.x already persists periodic statistics to <c>DbDir/LOG</c>
        /// when <see cref="EnableStatistics"/> and <see cref="StatsDumpPeriodSec"/> are set; enable this only when
        /// operators want duplicate snapshots in the centralized host log pipeline (required workaround on some 6.x
        /// builds).</para>
        /// </remarks>
        public bool MirrorStatsToHostLogger { get; set; }
    }
}
