// <copyright file="RocksHistoryBloomFilterTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Rocks;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Verifies RocksDB Bloom filter configuration for HistoryDB column families.
    /// </summary>
    [TestFixture]
    public sealed class RocksHistoryBloomFilterTests
    {
        /// <summary>
        /// Ensures digest Bloom defaults allow open, persist, and point lookup with filters enabled.
        /// </summary>
        [Test]
        public void Open_WithDefaultDigestBloom_PutReservationSucceeds()
        {
            string dir = CreateTempDbDir();
            try
            {
                HistoryDbOptions options = new()
                {
                    DbDir = dir,
                    RememberDays = 2,
                    RocksDb = new HistoryRocksDbOptions
                    {
                        DigestBloomBitsPerKey = HistoryRocksDbOptions.DefaultDigestBloomBitsPerKey,
                        BlockCacheBytes = 8 * 1024 * 1024,
                        BlockSizeBytes = 4096,
                    },
                };
                using RocksHistoryStore store = new(
                    Options.Create(options),
                    new HistoryMetrics(),
                    NullLogger<RocksHistoryStore>.Instance);
                Span<byte> digest = stackalloc byte[32];
                digest.Fill(0xAB);
                ulong exp = (ulong)DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
                store.PutReservation(digest, exp);
                Assert.That(store.GetDigestExpiration(digest), Is.EqualTo(exp));
            }
            finally
            {
                DeleteTempDbDir(dir);
            }
        }

        /// <summary>
        /// Ensures digest Bloom can be disabled without breaking persistence semantics.
        /// </summary>
        [Test]
        public void Open_WithDigestBloomDisabled_PutReservationSucceeds()
        {
            string dir = CreateTempDbDir();
            try
            {
                HistoryDbOptions options = new()
                {
                    DbDir = dir,
                    RememberDays = 2,
                    RocksDb = new HistoryRocksDbOptions
                    {
                        DigestBloomBitsPerKey = 0,
                    },
                };
                using RocksHistoryStore store = new(
                    Options.Create(options),
                    new HistoryMetrics(),
                    NullLogger<RocksHistoryStore>.Instance);
                Span<byte> digest = stackalloc byte[32];
                digest.Fill(0xBC);
                ulong exp = (ulong)DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
                store.PutReservation(digest, exp);
                Assert.That(store.GetDigestExpiration(digest), Is.EqualTo(exp));
            }
            finally
            {
                DeleteTempDbDir(dir);
            }
        }

        /// <summary>
        /// Ensures expiration Bloom and memtable prefix Bloom knobs can be enabled together.
        /// </summary>
        [Test]
        public void Open_WithExpirationBloomOptions_PutReservationSucceeds()
        {
            string dir = CreateTempDbDir();
            try
            {
                HistoryDbOptions options = new()
                {
                    DbDir = dir,
                    RememberDays = 2,
                    RocksDb = new HistoryRocksDbOptions
                    {
                        DigestBloomBitsPerKey = 12,
                        ExpirationBloomBitsPerKey = 8,
                        ExpirationMemtablePrefixBloomRatio = 0.1,
                    },
                };
                using RocksHistoryStore store = new(
                    Options.Create(options),
                    new HistoryMetrics(),
                    NullLogger<RocksHistoryStore>.Instance);
                Span<byte> digest = stackalloc byte[32];
                digest.Fill(0xCD);
                ulong exp = (ulong)DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
                store.PutReservation(digest, exp);
                Assert.That(store.CountExpirationKeys(), Is.EqualTo(1));
            }
            finally
            {
                DeleteTempDbDir(dir);
            }
        }

        /// <summary>
        /// Creates a unique temporary database directory.
        /// </summary>
        /// <returns>Absolute directory path.</returns>
        private static string CreateTempDbDir()
        {
            return Path.Combine(Path.GetTempPath(), "historydb-bloom-test-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Deletes a temporary database directory when present.
        /// </summary>
        /// <param name="dir">Directory path.</param>
        private static void DeleteTempDbDir(string dir)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
