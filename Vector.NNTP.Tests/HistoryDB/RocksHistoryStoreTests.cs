// <copyright file="RocksHistoryStoreTests.cs" company="Usenet Ninja">
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
    /// RocksDB dual-CF integration tests using a temporary database directory.
    /// </summary>
    [TestFixture]
    public sealed class RocksHistoryStoreTests
    {
        /// <summary>
        /// Verifies WriteBatch maintains digest and expiration index pairs.
        /// </summary>
        [Test]
        public void PutReservation_MaintainsDigestAndExpirationKeys()
        {
            string dir = Path.Combine(Path.GetTempPath(), "historydb-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = Options.Create(new HistoryDbOptions { DbDir = dir, RememberDays = 2 });
                using var store = new RocksHistoryStore(options, new HistoryMetrics(), NullLogger<RocksHistoryStore>.Instance);
                Span<byte> digest = stackalloc byte[32];
                digest.Fill(0xCD);
                ulong exp = (ulong)DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
                store.PutReservation(digest, exp);
                Assert.That(store.GetDigestExpiration(digest), Is.EqualTo(exp));
                Assert.That(store.CountExpirationKeys(), Is.EqualTo(1));

                store.PutReservation(digest, exp + 100);
                Assert.That(store.GetDigestExpiration(digest), Is.EqualTo(exp + 100));
                Assert.That(store.CountExpirationKeys(), Is.EqualTo(1));
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
