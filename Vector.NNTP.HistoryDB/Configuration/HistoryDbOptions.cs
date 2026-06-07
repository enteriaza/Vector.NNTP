// <copyright file="HistoryDbOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.HistoryDB.Configuration
{
    /// <summary>
    /// History database paths, retention, memory cache, rebuild, and RocksDB tuning.
    /// </summary>
    internal sealed class HistoryDbOptions
    {
        /// <summary>
        /// Configuration section name under <c>NntpServer</c>.
        /// </summary>
        public const string SectionName = "NntpServer:HistoryDb";

        /// <summary>
        /// Current Rocks schema version stored in metadata.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// Gets or sets the RocksDB directory path.
        /// </summary>
        [Required]
        public string DbDir { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the retention window in days for duplicate suppression.
        /// </summary>
        [Range(1, 3650)]
        public int RememberDays { get; set; } = 2;

        /// <summary>
        /// Gets or sets the in-memory hot cache byte budget.
        /// </summary>
        [Range(1048576, long.MaxValue)]
        public long MemoryLimitBytes { get; set; } = 1_073_741_824;

        /// <summary>
        /// Gets or sets the number of memory-cache shards (power of two; default 64).
        /// </summary>
        [Range(1, 256)]
        public int MemoryShardCount { get; set; } = 64;

        /// <summary>
        /// Gets or sets the bounded backfill queue capacity.
        /// </summary>
        [Range(1024, int.MaxValue)]
        public int QueueCapacity { get; set; } = 262_144;

        /// <summary>
        /// Gets or sets the Redis key prefix for history keys (may be empty).
        /// </summary>
        public string KeyPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether to preload memory from Rocks on startup after rebuild.
        /// </summary>
        public bool EnableMemoryPreloadOnStartup { get; set; } = true;

        /// <summary>
        /// Gets or sets how often rebuild checkpoints <c>history:rebuild_state</c> in Redis.
        /// </summary>
        [Range(1000, int.MaxValue)]
        public int RebuildCheckpointInterval { get; set; } = 50_000;

        /// <summary>
        /// Gets or sets the number of keys per Redis pipeline batch during rebuild.
        /// </summary>
        [Range(1, 100_000)]
        public int RebuildRedisBatchSize { get; set; } = 1000;

        /// <summary>
        /// Gets or sets optional RocksDB tuning overrides.
        /// </summary>
        public HistoryRocksDbOptions RocksDb { get; set; } = new();
    }
}
