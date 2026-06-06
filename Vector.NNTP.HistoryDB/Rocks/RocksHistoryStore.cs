// <copyright file="RocksHistoryStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RocksDbSharp;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.HistoryDB.Rocks
{
    /// <summary>
    /// Dual column-family RocksDB store for history digests and expiration ordering.
    /// </summary>
    internal sealed class RocksHistoryStore : IDisposable
    {
        /// <summary>
        /// Digest column family name.
        /// </summary>
        internal const string CfByDigest = "by_digest";

        /// <summary>
        /// Expiration-ordered column family name.
        /// </summary>
        internal const string CfByExpiration = "by_expiration";

        /// <summary>
        /// The tombstone value.
        /// </summary>
        private static readonly byte[] TombstoneValue = [1];

        /// <summary>
        /// The options.
        /// </summary>
        private readonly HistoryDbOptions _options;

        /// <summary>
        /// The metrics.
        /// </summary>
        private readonly HistoryMetrics _metrics;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<RocksHistoryStore> _logger;

        /// <summary>
        /// Open-time database options (kept alive for <see cref="DbOptions.GetStatisticsString"/>).
        /// </summary>
        private readonly DbOptions _dbOptions;

        /// <summary>
        /// Full path to the database directory.
        /// </summary>
        private readonly string _dbPath;

        /// <summary>
        /// The RocksDB database.
        /// </summary>
        private readonly RocksDb _db;

        /// <summary>
        /// The digest column family handle.
        /// </summary>
        private readonly ColumnFamilyHandle _digestCf;

        /// <summary>
        /// The expiration column family handle.
        /// </summary>
        private readonly ColumnFamilyHandle _expirationCf;

        /// <summary>
        /// Owns native Bloom filter and block-cache handles for the database lifetime.
        /// </summary>
        private readonly RocksHistoryBloomFilterConfigurator _bloomConfigurator;

        /// <summary>
        /// The expiration key scratch buffer.
        /// </summary>
        private readonly byte[] _expKeyScratch = new byte[HistoryRocksKeyEncoding.ExpirationKeyLength];

        /// <summary>
        /// The digest value scratch buffer.
        /// </summary>
        private readonly byte[] _digestValueScratch = new byte[HistoryRocksKeyEncoding.DigestValueLength];

        /// <summary>
        /// The digest key scratch buffer.
        /// </summary>
        private readonly byte[] _digestKeyScratch = new byte[HistoryKeyEncoder.DigestLength];

        /// <summary>
        /// Whether the store has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="RocksHistoryStore"/> class.
        /// </summary>
        /// <param name="options">History options.</param>
        /// <param name="metrics">Metrics.</param>
        /// <param name="logger">Logger.</param>
        public RocksHistoryStore(
            IOptions<HistoryDbOptions> options,
            HistoryMetrics metrics,
            ILogger<RocksHistoryStore> logger)
        {
            _options = options.Value;
            _metrics = metrics;
            _logger = logger;
            _dbPath = Path.GetFullPath(_options.DbDir);
            _ = Directory.CreateDirectory(_dbPath);

            HistoryRocksDbOptions rocks = _options.RocksDb;
            _dbOptions = new DbOptions()
                .SetCreateIfMissing(true)
                .SetCreateMissingColumnFamilies(true)
                .SetStatsDumpPeriodSec(rocks.StatsDumpPeriodSec);
            if (rocks.EnableStatistics)
            {
                _ = _dbOptions.EnableStatistics();
            }

            if (rocks.MaxBackgroundJobs > 0)
            {
                _ = _dbOptions.SetMaxBackgroundCompactions(rocks.MaxBackgroundJobs);
            }

            if (rocks.StatsDumpPeriodSec > 0 && rocks.EnableStatistics)
            {
                _logger.LogInformation(
                    "RocksDB statistics enabled at {DbDir}; native stats_dump_period_sec={StatsDumpPeriodSec}. Host logger snapshots use the same interval (RocksDbSharp 6.2.x may not emit periodic LOG dumps).",
                    _dbPath,
                    rocks.StatsDumpPeriodSec);
            }
            else if (rocks.StatsDumpPeriodSec > 0 && !rocks.EnableStatistics)
            {
                _logger.LogWarning(
                    "RocksDB StatsDumpPeriodSec is {StatsDumpPeriodSec} but EnableStatistics is false; stats snapshots will not include ticker data",
                    rocks.StatsDumpPeriodSec);
            }

            _bloomConfigurator = new RocksHistoryBloomFilterConfigurator();
            ColumnFamilyOptions digestCfOptions = _bloomConfigurator.CreateDigestColumnFamilyOptions(rocks);
            ColumnFamilyOptions expirationCfOptions = _bloomConfigurator.CreateExpirationColumnFamilyOptions(rocks);
            ColumnFamilies families = new()
            {
                { CfByDigest, digestCfOptions },
                { CfByExpiration, expirationCfOptions },
            };

            _db = RocksDb.Open(_dbOptions, _dbPath, families);
            _logger.LogInformation(
                "RocksDB opened at {DbDir} with digest BloomBitsPerKey={DigestBloomBitsPerKey}, expiration BloomBitsPerKey={ExpirationBloomBitsPerKey}, BlockCacheBytes={BlockCacheBytes}, BlockSizeBytes={BlockSizeBytes}.",
                _dbPath,
                rocks.DigestBloomBitsPerKey,
                rocks.ExpirationBloomBitsPerKey,
                rocks.BlockCacheBytes,
                rocks.BlockSizeBytes);
            _digestCf = _db.GetColumnFamily(CfByDigest);
            _expirationCf = _db.GetColumnFamily(CfByExpiration);
        }

        /// <summary>
        /// Persists digest and expiration index atomically.
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <param name="expirationEpochSeconds">New expiration epoch.</param>
        public void PutReservation(ReadOnlySpan<byte> digest, ulong expirationEpochSeconds)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ulong oldExp = 0;
            bool hadOld = false;
            byte[]? existing = _db.Get(digest.ToArray(), _digestCf);
            if (existing is { Length: HistoryRocksKeyEncoding.DigestValueLength })
            {
                oldExp = HistoryRocksKeyEncoding.DecodeDigestValue(existing);
                hadOld = true;
                if (expirationEpochSeconds <= oldExp)
                {
                    return;
                }
            }

            using WriteBatch batch = new();
            if (hadOld)
            {
                HistoryRocksKeyEncoding.EncodeExpirationKey(oldExp, digest, _expKeyScratch);
                _ = batch.Delete(_expKeyScratch, _expirationCf);
            }

            HistoryRocksKeyEncoding.EncodeExpirationKey(expirationEpochSeconds, digest, _expKeyScratch);
            _ = batch.Put(_expKeyScratch, TombstoneValue, _expirationCf);
            HistoryRocksKeyEncoding.EncodeDigestValue(expirationEpochSeconds, _digestValueScratch);
            _ = batch.Put(digest.ToArray(), _digestValueScratch, _digestCf);
            _db.Write(batch);
        }

        /// <summary>
        /// Deletes expired keys from the expiration prefix.
        /// </summary>
        /// <param name="nowEpochSeconds">Current UTC epoch.</param>
        /// <param name="maxDeletes">Maximum deletes per sweep pass.</param>
        /// <returns>Number of keys deleted.</returns>
        public int SweepExpired(ulong nowEpochSeconds, int maxDeletes)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long start = Environment.TickCount64;
            int deleted = 0;
            using Iterator it = _db.NewIterator(_expirationCf);
            _ = it.SeekToFirst();
            using WriteBatch batch = new();
            while (it.Valid() && deleted < maxDeletes)
            {
                ReadOnlySpan<byte> key = it.Key();
                if (!HistoryRocksKeyEncoding.TryDecodeExpirationKey(key, out ulong exp, _digestKeyScratch))
                {
                    break;
                }

                if (exp > nowEpochSeconds)
                {
                    break;
                }

                _ = batch.Delete(key.ToArray(), _expirationCf);
                _ = batch.Delete(_digestKeyScratch, _digestCf);
                deleted++;
                _ = it.Next();
            }

            if (deleted > 0)
            {
                _db.Write(batch);
            }

            _metrics.RecordSweepMilliseconds(Environment.TickCount64 - start);
            return deleted;
        }

        /// <summary>
        /// Rebuilds Redis from unexpired expiration keys.
        /// </summary>
        /// <param name="nowEpochSeconds">Current epoch.</param>
        /// <param name="resumeKey">Optional resume key (40 bytes).</param>
        /// <param name="processBatch">Batch processor.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Total keys processed in this run.</returns>
        public async Task<long> RebuildForwardAsync(
            ulong nowEpochSeconds,
            byte[]? resumeKey,
            Func<IReadOnlyList<(byte[] ExpirationKey, ulong Expiration, byte[] Digest)>, CancellationToken, Task> processBatch,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long processed = 0;
            int batchSize = _options.RebuildRedisBatchSize;
            List<(byte[] ExpirationKey, ulong Expiration, byte[] Digest)> batch = new(batchSize);
            using Iterator it = _db.NewIterator(_expirationCf);
            if (resumeKey is { Length: HistoryRocksKeyEncoding.ExpirationKeyLength })
            {
                _ = it.Seek(resumeKey);
                _ = it.Next();
            }
            else
            {
                _ = it.SeekToFirst();
            }

            while (it.Valid())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> key = it.Key();
                if (!HistoryRocksKeyEncoding.TryDecodeExpirationKey(key, out ulong exp, _digestKeyScratch))
                {
                    break;
                }

                if (exp <= nowEpochSeconds)
                {
                    _ = it.Next();
                    continue;
                }

                batch.Add((key.ToArray(), exp, _digestKeyScratch.ToArray()));
                processed++;
                if (batch.Count >= batchSize)
                {
                    long batchStart = Environment.TickCount64;
                    await processBatch(batch, cancellationToken).ConfigureAwait(false);
                    _metrics.RecordRebuildBatchMilliseconds(Environment.TickCount64 - batchStart);
                    batch.Clear();
                }

                _ = it.Next();
            }

            if (batch.Count > 0)
            {
                await processBatch(batch, cancellationToken).ConfigureAwait(false);
            }

            return processed;
        }

        /// <summary>
        /// Preloads memory cache from highest expiration keys.
        /// </summary>
        /// <param name="nowEpochSeconds">Current epoch.</param>
        /// <param name="insert">Insert callback.</param>
        /// <param name="byteBudget">Maximum bytes to load.</param>
        /// <returns>Entries loaded.</returns>
        public int PreloadReverse(ulong nowEpochSeconds, Action<byte[], ulong> insert, long byteBudget)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long start = Environment.TickCount64;
            long bytes = 0;
            int loaded = 0;
            const int EntryBytes = HistoryKeyEncoder.DigestLength + 8;
            using Iterator it = _db.NewIterator(_expirationCf);
            _ = it.SeekToLast();
            while (it.Valid() && bytes < byteBudget)
            {
                ReadOnlySpan<byte> key = it.Key();
                if (!HistoryRocksKeyEncoding.TryDecodeExpirationKey(key, out ulong exp, _digestKeyScratch))
                {
                    break;
                }

                if (exp > nowEpochSeconds)
                {
                    insert([.. _digestKeyScratch], exp);
                    bytes += EntryBytes;
                    loaded++;
                }

                _ = it.Prev();
            }

            _metrics.RecordPreloadMilliseconds(Environment.TickCount64 - start);
            return loaded;
        }

        /// <summary>
        /// Disposes the store.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _db.Dispose();
        }

        /// <summary>
        /// Logs current RocksDB property and ticker statistics to the host logger.
        /// </summary>
        /// <remarks>
        /// Used by <see cref="HostedServices.HistoryRocksStatsLogHostedService"/> because native periodic LOG dumps are unreliable on RocksDbSharp 6.2.x.
        /// </remarks>
        internal void EmitStatsSnapshot()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_options.RocksDb.EnableStatistics)
            {
                return;
            }

            string dbStats = _db.GetProperty("rocksdb.stats");
            if (!string.IsNullOrWhiteSpace(dbStats))
            {
                _logger.LogInformation(
                    "RocksDB rocksdb.stats snapshot ({DbDir}):\n{Stats}",
                    _dbPath,
                    dbStats);
            }

            string tickerStats = _dbOptions.GetStatisticsString();
            if (!string.IsNullOrWhiteSpace(tickerStats))
            {
                _logger.LogInformation(
                    "RocksDB ticker statistics snapshot ({DbDir}):\n{Stats}",
                    _dbPath,
                    tickerStats);
            }
        }

        /// <summary>
        /// Reads the expiration epoch stored for a digest (test and diagnostics).
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <returns>Expiration epoch when present; otherwise <see langword="null"/>.</returns>
        internal ulong? GetDigestExpiration(ReadOnlySpan<byte> digest)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            byte[]? existing = _db.Get(digest.ToArray(), _digestCf);
            return existing is { Length: HistoryRocksKeyEncoding.DigestValueLength }
                ? HistoryRocksKeyEncoding.DecodeDigestValue(existing)
                : null;
        }

        /// <summary>
        /// Counts keys in the expiration column family (tests).
        /// </summary>
        /// <returns>Key count from iterator.</returns>
        internal int CountExpirationKeys()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int count = 0;
            using Iterator it = _db.NewIterator(_expirationCf);
            for (it.SeekToFirst(); it.Valid(); it.Next())
            {
                count++;
            }

            return count;
        }
    }
}
