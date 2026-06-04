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
            this._options = options.Value;
            this._metrics = metrics;
            this._logger = logger;
            string path = Path.GetFullPath(this._options.DbDir);
            Directory.CreateDirectory(path);

            DbOptions dbOptions = new DbOptions()
                .SetCreateIfMissing(true)
                .SetCreateMissingColumnFamilies(true)
                .SetStatsDumpPeriodSec(600);

            ColumnFamilyOptions cfOptions = new ColumnFamilyOptions();
            HistoryRocksDbOptions rocks = this._options.RocksDb;
            if (rocks.WriteBufferBytes > 0)
            {
                _ = cfOptions.SetWriteBufferSize((ulong)rocks.WriteBufferBytes);
            }

            if (rocks.MaxWriteBufferNumber > 0)
            {
                _ = cfOptions.SetMaxWriteBufferNumber(rocks.MaxWriteBufferNumber);
            }

            ColumnFamilies families = new ColumnFamilies
            {
                { CfByDigest, cfOptions },
                { CfByExpiration, cfOptions },
            };

            this._db = RocksDb.Open(dbOptions, path, families);
            this._digestCf = this._db.GetColumnFamily(CfByDigest);
            this._expirationCf = this._db.GetColumnFamily(CfByExpiration);
        }

        /// <summary>
        /// Persists digest and expiration index atomically.
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <param name="expirationEpochSeconds">New expiration epoch.</param>
        public void PutReservation(ReadOnlySpan<byte> digest, ulong expirationEpochSeconds)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            ulong oldExp = 0;
            bool hadOld = false;
            byte[]? existing = this._db.Get(digest.ToArray(), this._digestCf);
            if (existing is { Length: HistoryRocksKeyEncoding.DigestValueLength })
            {
                oldExp = HistoryRocksKeyEncoding.DecodeDigestValue(existing);
                hadOld = true;
                if (expirationEpochSeconds <= oldExp)
                {
                    return;
                }
            }

            using WriteBatch batch = new WriteBatch();
            if (hadOld)
            {
                HistoryRocksKeyEncoding.EncodeExpirationKey(oldExp, digest, this._expKeyScratch);
                batch.Delete(this._expKeyScratch, this._expirationCf);
            }

            HistoryRocksKeyEncoding.EncodeExpirationKey(expirationEpochSeconds, digest, this._expKeyScratch);
            batch.Put(this._expKeyScratch, TombstoneValue, this._expirationCf);
            HistoryRocksKeyEncoding.EncodeDigestValue(expirationEpochSeconds, this._digestValueScratch);
            batch.Put(digest.ToArray(), this._digestValueScratch, this._digestCf);
            this._db.Write(batch);
        }

        /// <summary>
        /// Deletes expired keys from the expiration prefix.
        /// </summary>
        /// <param name="nowEpochSeconds">Current UTC epoch.</param>
        /// <param name="maxDeletes">Maximum deletes per sweep pass.</param>
        /// <returns>Number of keys deleted.</returns>
        public int SweepExpired(ulong nowEpochSeconds, int maxDeletes)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            long start = Environment.TickCount64;
            int deleted = 0;
            using Iterator it = this._db.NewIterator(this._expirationCf);
            it.SeekToFirst();
            using WriteBatch batch = new WriteBatch();
            while (it.Valid() && deleted < maxDeletes)
            {
                ReadOnlySpan<byte> key = it.Key();
                if (!HistoryRocksKeyEncoding.TryDecodeExpirationKey(key, out ulong exp, this._digestKeyScratch))
                {
                    break;
                }

                if (exp > nowEpochSeconds)
                {
                    break;
                }

                batch.Delete(key.ToArray(), this._expirationCf);
                batch.Delete(this._digestKeyScratch, this._digestCf);
                deleted++;
                it.Next();
            }

            if (deleted > 0)
            {
                this._db.Write(batch);
            }

            this._metrics.RecordSweepMilliseconds(Environment.TickCount64 - start);
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
            ObjectDisposedException.ThrowIf(this._disposed, this);
            long processed = 0;
            int batchSize = this._options.RebuildRedisBatchSize;
            List<(byte[] ExpirationKey, ulong Expiration, byte[] Digest)> batch = new List<(byte[] ExpirationKey, ulong Expiration, byte[] Digest)>(batchSize);
            using Iterator it = this._db.NewIterator(this._expirationCf);
            if (resumeKey is { Length: HistoryRocksKeyEncoding.ExpirationKeyLength })
            {
                it.Seek(resumeKey);
                it.Next();
            }
            else
            {
                it.SeekToFirst();
            }

            while (it.Valid())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> key = it.Key();
                if (!HistoryRocksKeyEncoding.TryDecodeExpirationKey(key, out ulong exp, this._digestKeyScratch))
                {
                    break;
                }

                if (exp <= nowEpochSeconds)
                {
                    it.Next();
                    continue;
                }

                batch.Add((key.ToArray(), exp, this._digestKeyScratch.ToArray()));
                processed++;
                if (batch.Count >= batchSize)
                {
                    long batchStart = Environment.TickCount64;
                    await processBatch(batch, cancellationToken).ConfigureAwait(false);
                    this._metrics.RecordRebuildBatchMilliseconds(Environment.TickCount64 - batchStart);
                    batch.Clear();
                }

                it.Next();
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
            ObjectDisposedException.ThrowIf(this._disposed, this);
            long start = Environment.TickCount64;
            long bytes = 0;
            int loaded = 0;
            const int entryBytes = HistoryKeyEncoder.DigestLength + 8;
            using Iterator it = this._db.NewIterator(this._expirationCf);
            it.SeekToLast();
            while (it.Valid() && bytes < byteBudget)
            {
                ReadOnlySpan<byte> key = it.Key();
                if (!HistoryRocksKeyEncoding.TryDecodeExpirationKey(key, out ulong exp, this._digestKeyScratch))
                {
                    break;
                }

                if (exp > nowEpochSeconds)
                {
                    insert(this._digestKeyScratch.ToArray(), exp);
                    bytes += entryBytes;
                    loaded++;
                }

                it.Prev();
            }

            this._metrics.RecordPreloadMilliseconds(Environment.TickCount64 - start);
            return loaded;
        }

        /// <summary>
        /// Disposes the store.
        /// </summary>
        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            this._db.Dispose();
        }

        /// <summary>
        /// Reads the expiration epoch stored for a digest (test and diagnostics).
        /// </summary>
        /// <param name="digest">32-byte digest.</param>
        /// <returns>Expiration epoch when present; otherwise <see langword="null"/>.</returns>
        internal ulong? GetDigestExpiration(ReadOnlySpan<byte> digest)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            byte[]? existing = this._db.Get(digest.ToArray(), this._digestCf);
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
            ObjectDisposedException.ThrowIf(this._disposed, this);
            int count = 0;
            using Iterator it = this._db.NewIterator(this._expirationCf);
            for (it.SeekToFirst(); it.Valid(); it.Next())
            {
                count++;
            }

            return count;
        }
    }
}
