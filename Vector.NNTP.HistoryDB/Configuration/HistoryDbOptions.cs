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
        /// On-disk RocksDB data directory (required at startup).
        /// </summary>
        [Required]
        public string DbDir { get; set; } = string.Empty;

        /// <summary>
        /// Duplicate-suppression retention window in whole UTC days.
        /// </summary>
        [Range(1, 3650)]
        public int RememberDays { get; set; } = 2;

        /// <summary>
        /// Logical byte budget for the in-memory hot cache across all shards.
        /// </summary>
        [Range(1048576, long.MaxValue)]
        public long MemoryLimitBytes { get; set; } = 1_073_741_824;

        /// <summary>
        /// Number of memory-cache shards; must be a power of two (default 64).
        /// </summary>
        [Range(1, 256)]
        public int MemoryShardCount { get; set; } = 64;

        /// <summary>
        /// Bounded capacity of the Rocks backfill queue after Redis reserve.
        /// </summary>
        [Range(1024, int.MaxValue)]
        public int QueueCapacity { get; set; } = 262_144;

        /// <summary>
        /// Optional Redis key prefix prepended to history keys (may be empty).
        /// </summary>
        public string KeyPrefix { get; set; } = string.Empty;

        /// <summary>
        /// When true, preloads the memory cache from Rocks after startup rebuild completes.
        /// </summary>
        public bool EnableMemoryPreloadOnStartup { get; set; } = true;

        /// <summary>
        /// Keys processed between Redis <c>history:rebuild_state</c> checkpoints during rebuild.
        /// </summary>
        [Range(1000, int.MaxValue)]
        public int RebuildCheckpointInterval { get; set; } = 50_000;

        /// <summary>
        /// Number of history keys per Redis pipeline batch during rebuild.
        /// </summary>
        [Range(1, 100_000)]
        public int RebuildRedisBatchSize { get; set; } = 1000;

        /// <summary>
        /// Nested RocksDB tuning overrides for digest and expiration column families.
        /// </summary>
        public HistoryRocksDbOptions RocksDb { get; set; } = new();
    }
}
