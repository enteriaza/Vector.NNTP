// <copyright file="HistoryMemoryCacheEvictionTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Verifies min-heap eviction ordering in <see cref="HistoryMemoryCache"/>.
    /// </summary>
    [TestFixture]
    public sealed class HistoryMemoryCacheEvictionTests
    {
        /// <summary>
        /// Bytes per cache entry (digest + expiration).
        /// </summary>
        private const int BytesPerEntry = HistoryKeyEncoder.DigestLength + 8;

        /// <summary>
        /// Verifies lowest-expiration entries are evicted first when over budget.
        /// </summary>
        [Test]
        public void EvictIfNeeded_RemovesLowestExpirationFirst()
        {
            var metrics = new HistoryMetrics();
            var cache = new HistoryMemoryCache(BytesPerEntry * 2, shardCount: 1, metrics);
            ulong now = 1000;

            DigestKey keyLow = CreateKey(1);
            DigestKey keyMid = CreateKey(2);
            DigestKey keyHigh = CreateKey(3);

            cache.InsertOrUpdate(in keyLow, now + 100);
            cache.InsertOrUpdate(in keyMid, now + 200);
            cache.InsertOrUpdate(in keyHigh, now + 300);

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGetDuplicate(in keyLow, now), Is.False);
            Assert.That(cache.TryGetDuplicate(in keyMid, now), Is.True);
            Assert.That(cache.TryGetDuplicate(in keyHigh, now), Is.True);
        }

        /// <summary>
        /// Verifies expiration bump tombstones do not cause premature eviction of updated keys.
        /// </summary>
        [Test]
        public void EvictIfNeeded_IgnoresStaleHeapTombstonesAfterExpirationBump()
        {
            var metrics = new HistoryMetrics();
            var cache = new HistoryMemoryCache(BytesPerEntry * 2, shardCount: 1, metrics);
            ulong now = 1000;

            DigestKey keyA = CreateKey(10);
            DigestKey keyB = CreateKey(20);
            DigestKey keyC = CreateKey(30);

            cache.InsertOrUpdate(in keyA, now + 100);
            cache.InsertOrUpdate(in keyB, now + 200);
            cache.InsertOrUpdate(in keyA, now + 500);
            cache.InsertOrUpdate(in keyC, now + 300);

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGetDuplicate(in keyB, now), Is.False);
            Assert.That(cache.TryGetDuplicate(in keyA, now), Is.True);
            Assert.That(cache.TryGetDuplicate(in keyC, now), Is.True);
        }

        /// <summary>
        /// Builds a deterministic digest key from a seed byte.
        /// </summary>
        /// <param name="seed">First digest byte seed.</param>
        /// <returns>Digest key.</returns>
        private static DigestKey CreateKey(byte seed)
        {
            Span<byte> digest = stackalloc byte[32];
            digest.Fill(seed);
            return new DigestKey(digest);
        }
    }
}
