// <copyright file="RocksHistoryStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 230-249.

namespace Vector.NNTP.HistoryDB.Rocks
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="RocksHistoryStore"/>.
    /// </summary>
    /// <remarks>
    /// Implementations bind to the primary-constructor <c>logger</c> parameter from <see cref="RocksHistoryStore"/>.
    /// </remarks>
    internal sealed partial class RocksHistoryStore
    {
        /// <summary>Logs RocksDB statistics enablement.</summary>
        /// <param name="dbDir">Database directory.</param>
        /// <param name="statsDumpPeriodSec">Native stats dump period.</param>
        [LoggerMessage(EventId = 230, Level = LogLevel.Information,
            Message = "RocksDB statistics enabled at {DbDir}; native stats_dump_period_sec={StatsDumpPeriodSec}. Host logger snapshots use the same interval.")]
        private partial void LogStatisticsEnabled(string dbDir, uint statsDumpPeriodSec);

        /// <summary>Logs statistics misconfiguration.</summary>
        /// <param name="statsDumpPeriodSec">Configured dump period.</param>
        [LoggerMessage(EventId = 231, Level = LogLevel.Warning,
            Message = "RocksDB StatsDumpPeriodSec is {StatsDumpPeriodSec} but EnableStatistics is false; stats snapshots will not include ticker data.")]
        private partial void LogStatisticsDisabled(uint statsDumpPeriodSec);

        /// <summary>Logs database open configuration.</summary>
        /// <param name="rocksDbVersion">Native RocksDB version string.</param>
        /// <param name="dbDir">Database directory.</param>
        /// <param name="digestBloomBitsPerKey">Digest Bloom bits per key.</param>
        /// <param name="expirationBloomBitsPerKey">Expiration Bloom bits per key.</param>
        /// <param name="digestBlockCacheBytes">Digest block cache bytes.</param>
        /// <param name="expirationBlockCacheBytes">Expiration block cache bytes.</param>
        /// <param name="blockSizeBytes">SST block size bytes.</param>
        /// <param name="maxBackgroundJobs">Background compaction threads.</param>
        [LoggerMessage(EventId = 232, Level = LogLevel.Information,
            Message = "RocksDB {RocksDbVersion} opened at {DbDir} with digest BloomBitsPerKey={DigestBloomBitsPerKey}, expiration BloomBitsPerKey={ExpirationBloomBitsPerKey}, DigestBlockCacheBytes={DigestBlockCacheBytes}, ExpirationBlockCacheBytes={ExpirationBlockCacheBytes}, BlockSizeBytes={BlockSizeBytes}, MaxBackgroundJobs={MaxBackgroundJobs}.")]
        private partial void LogDatabaseOpened(
            string rocksDbVersion,
            string dbDir,
            int digestBloomBitsPerKey,
            int expirationBloomBitsPerKey,
            long digestBlockCacheBytes,
            long expirationBlockCacheBytes,
            int blockSizeBytes,
            int maxBackgroundJobs);

        /// <summary>Logs a rocksdb.stats snapshot.</summary>
        /// <param name="dbDir">Database directory.</param>
        /// <param name="stats">Stats payload.</param>
        [LoggerMessage(EventId = 233, Level = LogLevel.Information,
            Message = "RocksDB rocksdb.stats snapshot ({DbDir}):\n{Stats}")]
        private partial void LogDbStatsSnapshot(string dbDir, string stats);

        /// <summary>Logs ticker statistics snapshot.</summary>
        /// <param name="dbDir">Database directory.</param>
        /// <param name="stats">Ticker stats payload.</param>
        [LoggerMessage(EventId = 234, Level = LogLevel.Information,
            Message = "RocksDB ticker statistics snapshot ({DbDir}):\n{Stats}")]
        private partial void LogTickerStatsSnapshot(string dbDir, string stats);
    }
}
