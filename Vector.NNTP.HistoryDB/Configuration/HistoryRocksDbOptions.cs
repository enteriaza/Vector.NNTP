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
    }
}
