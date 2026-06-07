// <copyright file="RocksHistoryStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
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
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    internal sealed partial class RocksHistoryStore(
        ILogger<RocksHistoryStore> logger) : IDisposable
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
        /// Single-byte tombstone value written to <c>by_expiration</c> when digest rows are swept.
        /// </summary>
        private static readonly byte[] TombstoneValue = [1];

        /// <summary>
        /// Bound <see cref="HistoryDbOptions"/> including database path and Rocks tuning.
        /// </summary>
        private readonly HistoryDbOptions _options = null!;

        /// <summary>
        /// HistoryDB metrics recorder for rebuild, sweep, and stats snapshot timing.
        /// </summary>
        private readonly HistoryMetrics _metrics = null!;

        /// <summary>
        /// Open-time database options (kept alive for ticker statistics snapshots via <c>GetStatisticsString</c>).
        /// </summary>
        private readonly DbOptions _dbOptions = null!;

        /// <summary>
        /// Full path to the database directory.
        /// </summary>
        private readonly string _dbPath = null!;

        /// <summary>
        /// Open RocksDB database handle for digest and expiration column families.
        /// </summary>
        private readonly RocksDb _db = null!;

        /// <summary>
        /// Column family handle for fixed-width digest keys (<c>by_digest</c>).
        /// </summary>
        private readonly ColumnFamilyHandle _digestCf = null!;

        /// <summary>
        /// Column family handle for expiration-ordered keys (<c>by_expiration</c>).
        /// </summary>
        private readonly ColumnFamilyHandle _expirationCf = null!;

        /// <summary>
        /// Owns native Bloom filter and block-cache handles for the database lifetime.
        /// </summary>
        private readonly RocksHistoryBloomFilterConfigurator _bloomConfigurator = null!;

        /// <summary>
        /// Reusable buffer for encoded <c>by_expiration</c> keys to avoid per-write allocations.
        /// </summary>
        private readonly byte[] _expKeyScratch = new byte[HistoryRocksKeyEncoding.ExpirationKeyLength];

        /// <summary>
        /// Reusable buffer for encoded digest values stored under <c>by_digest</c>.
        /// </summary>
        private readonly byte[] _digestValueScratch = new byte[HistoryRocksKeyEncoding.DigestValueLength];

        /// <summary>
        /// Reusable buffer for raw 32-byte digest keys written to <c>by_digest</c>.
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
        /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
        public RocksHistoryStore(
            IOptions<HistoryDbOptions> options,
            HistoryMetrics metrics,
            ILogger<RocksHistoryStore> logger)
            : this(logger)
        {
            _options = options.Value;
            _metrics = metrics;
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
                // RocksDB 10.4.x C# bindings expose compaction/flush knobs separately; map the unified jobs config
                // to compaction threads (same production mapping as the prior 6.2.2 host).
                _ = _dbOptions.SetMaxBackgroundCompactions(rocks.MaxBackgroundJobs);
            }

            if (rocks.StatsDumpPeriodSec > 0 && rocks.EnableStatistics)
            {
                LogStatisticsEnabled(_dbPath, rocks.StatsDumpPeriodSec);
            }
            else if (rocks.StatsDumpPeriodSec > 0 && !rocks.EnableStatistics)
            {
                LogStatisticsDisabled(rocks.StatsDumpPeriodSec);
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
            string rocksVersion = _db.GetProperty("rocksdb.version") ?? "unknown";
            LogDatabaseOpened(
                rocksVersion,
                _dbPath,
                rocks.DigestBloomBitsPerKey,
                rocks.ExpirationBloomBitsPerKey,
                rocks.DigestBlockCacheBytes,
                rocks.ExpirationBlockCacheBytes,
                rocks.BlockSizeBytes,
                rocks.MaxBackgroundJobs);
            _digestCf = _db.GetColumnFamily(CfByDigest);
            _expirationCf = _db.GetColumnFamily(CfByExpiration);
        }

        /// <summary>
        /// Persists digest and expiration index atomically when the new expiration is newer than any existing row.
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <param name="expirationEpochSeconds">New expiration epoch.</param>
        /// <exception cref="ObjectDisposedException">Thrown when the store has been disposed.</exception>
        internal void PutReservation(ReadOnlySpan<byte> digest, ulong expirationEpochSeconds)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            digest.CopyTo(_digestKeyScratch);
            ulong oldExp = 0;
            bool hadOld = false;
            byte[]? existing = _db.Get(_digestKeyScratch, _digestCf);
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
            _ = batch.Put(_digestKeyScratch, _digestValueScratch, _digestCf);
            _db.Write(batch);
        }

        /// <summary>
        /// Deletes expired keys from the expiration prefix.
        /// </summary>
        /// <param name="nowEpochSeconds">Current UTC epoch.</param>
        /// <param name="maxDeletes">Maximum deletes per sweep pass.</param>
        /// <returns>Count of expiration and digest rows deleted in this sweep pass (capped by <paramref name="maxDeletes"/>).</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the store has been disposed.</exception>
        internal int SweepExpired(ulong nowEpochSeconds, int maxDeletes)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long startTimestamp = Stopwatch.GetTimestamp();
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

            double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            _metrics.RecordSweepMilliseconds(elapsedMs);
            _metrics.RecordSweepDeleted(deleted);
            return deleted;
        }

        /// <summary>
        /// Rebuilds Redis from unexpired expiration keys.
        /// </summary>
        /// <param name="nowEpochSeconds">Current epoch.</param>
        /// <param name="resumeKey">Optional resume key (40 bytes).</param>
        /// <param name="processBatch">Batch processor.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Total unexpired expiration keys scanned and passed to <paramref name="processBatch"/>.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the store has been disposed.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        internal async Task<long> RebuildForwardAsync(
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
                    long batchStartTimestamp = Stopwatch.GetTimestamp();
                    await processBatch(batch, cancellationToken).ConfigureAwait(false);
                    _metrics.RecordRebuildBatchMilliseconds(Stopwatch.GetElapsedTime(batchStartTimestamp).TotalMilliseconds);
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
        /// Deletes digest and paired expiration index rows when present (spool release path).
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <returns><see langword="true"/> when a <c>by_digest</c> row existed and was deleted.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the store has been disposed.</exception>
        internal bool DeleteByDigest(ReadOnlySpan<byte> digest)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            digest.CopyTo(_digestKeyScratch);
            byte[]? existing = _db.Get(_digestKeyScratch, _digestCf);
            if (existing is not { Length: HistoryRocksKeyEncoding.DigestValueLength })
            {
                return false;
            }

            ulong expiration = HistoryRocksKeyEncoding.DecodeDigestValue(existing);
            using WriteBatch batch = new();
            HistoryRocksKeyEncoding.EncodeExpirationKey(expiration, digest, _expKeyScratch);
            _ = batch.Delete(_expKeyScratch, _expirationCf);
            _ = batch.Delete(_digestKeyScratch, _digestCf);
            _db.Write(batch);
            return true;
        }

        /// <summary>
        /// Preloads memory cache from highest expiration keys.
        /// </summary>
        /// <param name="nowEpochSeconds">Current epoch.</param>
        /// <param name="insert">Insert callback.</param>
        /// <param name="byteBudget">Maximum bytes to load.</param>
        /// <returns>Number of digest rows inserted via <paramref name="insert"/> before <paramref name="byteBudget"/> is reached.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the store has been disposed.</exception>
        internal int PreloadReverse(ulong nowEpochSeconds, Action<byte[], ulong> insert, long byteBudget)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long startTimestamp = Stopwatch.GetTimestamp();
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

            _metrics.RecordPreloadMilliseconds(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return loaded;
        }

        /// <summary>
        /// Releases the native RocksDB handle; subsequent calls throw <see cref="ObjectDisposedException"/>.
        /// </summary>
        /// <remarks>Idempotent; safe to call multiple times.</remarks>
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
        /// Used only when <see cref="HistoryRocksDbOptions.MirrorStatsToHostLogger"/> is enabled. Native RocksDB 10.x
        /// already persists the same statistics to <c>DbDir/LOG</c> via <c>stats_dump_period_sec</c>.
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
                LogDbStatsSnapshot(_dbPath, dbStats);
            }

            string tickerStats = _dbOptions.GetStatisticsString();
            if (!string.IsNullOrWhiteSpace(tickerStats))
            {
                LogTickerStatsSnapshot(_dbPath, tickerStats);
            }
        }

        /// <summary>
        /// Reads the expiration epoch stored for a digest (test and diagnostics).
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <returns>Expiration epoch when present; otherwise <see langword="null"/>.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the store has been disposed.</exception>
        internal ulong? GetDigestExpiration(ReadOnlySpan<byte> digest)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            digest.CopyTo(_digestKeyScratch);
            byte[]? existing = _db.Get(_digestKeyScratch, _digestCf);
            return existing is { Length: HistoryRocksKeyEncoding.DigestValueLength }
                ? HistoryRocksKeyEncoding.DecodeDigestValue(existing)
                : null;
        }

        /// <summary>
        /// Counts keys in the expiration column family (tests).
        /// </summary>
        /// <returns>Total keys in <c>by_expiration</c> via full iterator scan (test helper).</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the store has been disposed.</exception>
        internal int CountExpirationKeys()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int count = 0;
            using Iterator it = _db.NewIterator(_expirationCf);
            _ = it.SeekToFirst();
            while (it.Valid())
            {
                count++;
                _ = it.Next();
            }

            return count;
        }
    }
}
