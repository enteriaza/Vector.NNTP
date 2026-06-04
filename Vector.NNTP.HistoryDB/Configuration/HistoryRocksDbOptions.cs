// <copyright file="HistoryRocksDbOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.HistoryDB.Configuration
{
    /// <summary>
    /// RocksDB tuning knobs (initial defaults subject to benchmark validation).
    /// </summary>
    public sealed class HistoryRocksDbOptions
    {
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
        /// Gets or sets the target background flush/compaction jobs (0 = use implementation default).
        /// </summary>
        public int MaxBackgroundJobs { get; set; }

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
