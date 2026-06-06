// <copyright file="RocksHistoryOnDiskUpgradeTests.cs" company="Usenet Ninja">
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
    /// Verifies on-disk HistoryDB databases survive close and reopen with the current RocksDB bindings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RocksDB 10.x is expected to open databases written by the prior 6.2.2 native library without data loss.
    /// This test exercises the close/reopen path on a populated dual-CF database. Before production upgrade,
    /// operators should back up <c>DbDir</c> and validate open/read/write/sweep against a copy of the live directory.
    /// </para>
    /// <para>
    /// <b>Rollback:</b> Downgrading the native library after opening with a newer RocksDB version may be unsafe.
    /// Keep a backup taken before the first production open on the new bindings.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class RocksHistoryOnDiskUpgradeTests
    {
        /// <summary>
        /// Writes data, disposes the store, reopens the same directory, and verifies read/update/sweep semantics.
        /// </summary>
        [Test]
        public void Reopen_ExistingDatabase_ReadWriteAndSweepSucceed()
        {
            string dir = Path.Combine(Path.GetTempPath(), "historydb-upgrade-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                HistoryDbOptions options = new()
                {
                    DbDir = dir,
                    RememberDays = 2,
                    RocksDb = new HistoryRocksDbOptions
                    {
                        DigestBloomBitsPerKey = HistoryRocksDbOptions.DefaultDigestBloomBitsPerKey,
                        DigestBlockCacheBytes = 16 * 1024 * 1024,
                        ExpirationBlockCacheBytes = HistoryRocksDbOptions.DefaultExpirationBlockCacheBytes,
                    },
                };
                Span<byte> digest = stackalloc byte[32];
                digest.Fill(0x42);
                ulong futureExp = (ulong)DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
                ulong pastExp = (ulong)DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();

                using (RocksHistoryStore writer = new(
                    Options.Create(options),
                    new HistoryMetrics(),
                    NullLogger<RocksHistoryStore>.Instance))
                {
                    writer.PutReservation(digest, futureExp);
                    Span<byte> expiredDigest = stackalloc byte[32];
                    expiredDigest.Fill(0x43);
                    writer.PutReservation(expiredDigest, pastExp);
                    Assert.That(writer.CountExpirationKeys(), Is.EqualTo(2));
                }

                using RocksHistoryStore reader = new(
                    Options.Create(options),
                    new HistoryMetrics(),
                    NullLogger<RocksHistoryStore>.Instance);
                Assert.That(reader.GetDigestExpiration(digest), Is.EqualTo(futureExp));
                Assert.That(reader.CountExpirationKeys(), Is.EqualTo(2));

                ulong updatedExp = futureExp + 3600;
                reader.PutReservation(digest, updatedExp);
                Assert.That(reader.GetDigestExpiration(digest), Is.EqualTo(updatedExp));
                Assert.That(reader.CountExpirationKeys(), Is.EqualTo(2));

                ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int deleted = reader.SweepExpired(now, maxDeletes: 100);
                Assert.That(deleted, Is.EqualTo(1));
                Assert.That(reader.CountExpirationKeys(), Is.EqualTo(1));
                Assert.That(reader.GetDigestExpiration(digest), Is.EqualTo(updatedExp));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }
}
