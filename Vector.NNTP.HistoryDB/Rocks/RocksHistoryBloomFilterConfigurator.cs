// <copyright file="RocksHistoryBloomFilterConfigurator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RocksDB block-based Bloom filter and table factory wiring for HistoryDB column families.
//
// Builds per-CF ColumnFamilyOptions with native RocksDB BloomFilterPolicy instances. Native filter and cache handles
// must outlive the open database; this type owns them until Dispose.

using RocksDbSharp;
using Vector.NNTP.HistoryDB.Configuration;

namespace Vector.NNTP.HistoryDB.Rocks
{
    /// <summary>
    /// Configures RocksDB block-based Bloom filters and table factories for HistoryDB column families.
    /// </summary>
    /// <remarks>
    /// <para><b>Hot path impact:</b> Bloom filters accelerate <c>by_digest</c> point lookups during
    /// <see cref="RocksHistoryStore.PutReservation"/> by skipping SST blocks when a digest is absent. CHECK remains
    /// memory→Redis only; this is a cold-path Rocks optimization.</para>
    /// <para><b>Digest CF:</b> Fixed 32-byte keys use whole-key filtering with a block-based Bloom policy.</para>
    /// <para><b>Expiration CF:</b> Iterator-heavy (sweep/rebuild); Bloom is optional and disabled by default.</para>
    /// <para><b>Lifetime:</b> <see cref="BloomFilterPolicy"/>, <see cref="Cache"/>, and table factories are retained on
    /// this instance for the open database because RocksDB keeps native pointers after
    /// <see cref="RocksDb.Open(DbOptions, string, ColumnFamilies)"/>.</para>
    /// </remarks>
    internal sealed class RocksHistoryBloomFilterConfigurator
    {
        /// <summary>
        /// Shared LRU block cache reused across column families when configured.
        /// </summary>
        private Cache? _blockCache;

        /// <summary>
        /// Bloom policies kept alive for the database lifetime.
        /// </summary>
        private readonly List<BloomFilterPolicy> _bloomFilters = [];

        /// <summary>
        /// Table factories kept alive for the database lifetime.
        /// </summary>
        private readonly List<BlockBasedTableOptions> _tableFactories = [];

        /// <summary>
        /// Builds column-family options for the digest index used by point lookups.
        /// </summary>
        /// <param name="rocks">RocksDB tuning snapshot.</param>
        /// <returns>Configured <see cref="ColumnFamilyOptions"/> for <see cref="RocksHistoryStore.CfByDigest"/>.</returns>
        internal ColumnFamilyOptions CreateDigestColumnFamilyOptions(HistoryRocksDbOptions rocks)
        {
            ArgumentNullException.ThrowIfNull(rocks);
            ColumnFamilyOptions options = CreateBaseColumnFamilyOptions(rocks);
            BlockBasedTableOptions tableOptions = CreateBlockBasedTableOptions(
                rocks,
                rocks.DigestBloomBitsPerKey,
                wholeKeyFiltering: true);
            _ = options.SetBlockBasedTableFactory(tableOptions);
            if (rocks.OptimizeDigestFiltersForHits)
            {
                _ = options.SetOptimizeFiltersForHits(1);
            }

            return options;
        }

        /// <summary>
        /// Builds column-family options for the expiration-ordered index used by sweep and rebuild iterators.
        /// </summary>
        /// <param name="rocks">RocksDB tuning snapshot.</param>
        /// <returns>Configured <see cref="ColumnFamilyOptions"/> for <see cref="RocksHistoryStore.CfByExpiration"/>.</returns>
        internal ColumnFamilyOptions CreateExpirationColumnFamilyOptions(HistoryRocksDbOptions rocks)
        {
            ArgumentNullException.ThrowIfNull(rocks);
            ColumnFamilyOptions options = CreateBaseColumnFamilyOptions(rocks);
            if (rocks.ExpirationBloomBitsPerKey > 0)
            {
                BlockBasedTableOptions tableOptions = CreateBlockBasedTableOptions(
                    rocks,
                    rocks.ExpirationBloomBitsPerKey,
                    wholeKeyFiltering: true);
                _ = options.SetBlockBasedTableFactory(tableOptions);
            }

            if (rocks.ExpirationMemtablePrefixBloomRatio > 0)
            {
                _ = options.SetMemtablePrefixBloomSizeRatio(rocks.ExpirationMemtablePrefixBloomRatio);
            }

            return options;
        }

        /// <summary>
        /// Applies memtable sizing shared by both column families.
        /// </summary>
        /// <param name="rocks">RocksDB tuning snapshot.</param>
        /// <returns>Base column-family options before table-factory specialization.</returns>
        private static ColumnFamilyOptions CreateBaseColumnFamilyOptions(HistoryRocksDbOptions rocks)
        {
            ColumnFamilyOptions options = new();
            if (rocks.WriteBufferBytes > 0)
            {
                _ = options.SetWriteBufferSize((ulong)rocks.WriteBufferBytes);
            }

            if (rocks.MaxWriteBufferNumber > 0)
            {
                _ = options.SetMaxWriteBufferNumber(rocks.MaxWriteBufferNumber);
            }

            return options;
        }

        /// <summary>
        /// Creates block-based table options with an optional Bloom filter policy.
        /// </summary>
        /// <param name="rocks">RocksDB tuning snapshot.</param>
        /// <param name="bloomBitsPerKey">Bloom bits per key; zero disables the filter policy.</param>
        /// <param name="wholeKeyFiltering">Whether to use whole-key Bloom checks (recommended for fixed-width digest keys).</param>
        /// <returns>Configured table factory options.</returns>
        private BlockBasedTableOptions CreateBlockBasedTableOptions(
            HistoryRocksDbOptions rocks,
            int bloomBitsPerKey,
            bool wholeKeyFiltering)
        {
            BlockBasedTableOptions tableOptions = new();
            _tableFactories.Add(tableOptions);
            if (rocks.BlockSizeBytes > 0)
            {
                _ = tableOptions.SetBlockSize((ulong)rocks.BlockSizeBytes);
            }

            if (rocks.BlockCacheBytes > 0)
            {
                _blockCache ??= Cache.CreateLru((ulong)rocks.BlockCacheBytes);
                _ = tableOptions.SetBlockCache(_blockCache);
            }

            if (rocks.CacheIndexAndFilterBlocks)
            {
                _ = tableOptions.SetCacheIndexAndFilterBlocks(true);
            }

            if (rocks.PinL0FilterAndIndexBlocksInCache)
            {
                _ = tableOptions.SetPinL0FilterAndIndexBlocksInCache(true);
            }

            if (bloomBitsPerKey > 0)
            {
                BloomFilterPolicy bloomFilter = BloomFilterPolicy.Create(bloomBitsPerKey, true);
                _bloomFilters.Add(bloomFilter);
                _ = tableOptions.SetFilterPolicy(bloomFilter);
                _ = tableOptions.SetWholeKeyFiltering(wholeKeyFiltering);
            }

            return tableOptions;
        }
    }
}
