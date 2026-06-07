// <copyright file="HistoryMemoryCacheShardingTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Verifies digest routing and per-shard isolation in <see cref="HistoryMemoryCache"/>.
    /// </summary>
    [TestFixture]
    public sealed class HistoryMemoryCacheShardingTests
    {
        /// <summary>
        /// Verifies distinct digests can land on different shards and maintain independent state.
        /// </summary>
        [Test]
        public void InsertOrUpdate_DifferentShards_AreIndependent()
        {
            var metrics = new HistoryMetrics();
            var cache = new HistoryMemoryCache(1_073_741_824, shardCount: 64, metrics);
            ulong now = 1000;

            DigestKey keyA = CreateKeyWithLowBits(0x01);
            DigestKey keyB = CreateKeyWithLowBits(0x02);
            Assert.That(cache.GetShardIndexForKey(in keyA), Is.Not.EqualTo(cache.GetShardIndexForKey(in keyB)));

            cache.InsertOrUpdate(in keyA, now + 100);
            cache.InsertOrUpdate(in keyB, now + 200);

            Assert.That(cache.TryGetDuplicate(in keyA, now), Is.True);
            Assert.That(cache.TryGetDuplicate(in keyB, now), Is.True);
            Assert.That(cache.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies eviction budget applies per shard when keys share a shard.
        /// </summary>
        [Test]
        public void Eviction_PerShardBudget_EvictsWithinShardOnly()
        {
            const int logicalBytesPerEntry = HistoryMemoryCache.LogicalBytesPerEntry;
            var metrics = new HistoryMetrics();
            var cache = new HistoryMemoryCache(logicalBytesPerEntry * 2 * 64, shardCount: 64, metrics);
            ulong now = 1000;
            DigestKey probeKey = CreateKeyWithLowBits(0x05);
            int shard = cache.GetShardIndexForKey(in probeKey);

            DigestKey keyLow = CreateKeyWithLowBits((byte)shard);
            DigestKey keyMid = CreateKeyWithLowBits((byte)shard, sequenceByte: 1);
            DigestKey keyHigh = CreateKeyWithLowBits((byte)shard, sequenceByte: 2);

            cache.InsertOrUpdate(in keyLow, now + 100);
            cache.InsertOrUpdate(in keyMid, now + 200);
            cache.InsertOrUpdate(in keyHigh, now + 300);

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGetDuplicate(in keyLow, now), Is.False);
            Assert.That(cache.TryGetDuplicate(in keyMid, now), Is.True);
            Assert.That(cache.TryGetDuplicate(in keyHigh, now), Is.True);
        }

        /// <summary>
        /// Builds a digest with fixed low bits in <c>_w0</c> for deterministic shard selection.
        /// </summary>
        /// <param name="lowByte">Low byte of the digest (maps into shard mask).</param>
        /// <param name="sequenceByte">Optional high-byte discriminator.</param>
        /// <returns>Digest key.</returns>
        private static DigestKey CreateKeyWithLowBits(byte lowByte, byte sequenceByte = 0)
        {
            Span<byte> digest = stackalloc byte[32];
            digest[0] = lowByte;
            if (sequenceByte != 0)
            {
                digest[31] = sequenceByte;
            }

            return new DigestKey(digest);
        }
    }
}
