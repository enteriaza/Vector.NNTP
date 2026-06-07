// <copyright file="HistoryMemoryCacheAllocationTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.Tests.HistoryDB.Fakes;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Allocation tests for memory-hit duplicate detection.
    /// </summary>
    [TestFixture]
    public sealed class HistoryMemoryCacheAllocationTests
    {
        /// <summary>
        /// Verifies memory-hit CHECK path does not allocate (via cache API used on hot path).
        /// </summary>
        [Test]
        public void MemoryHit_TryGetDuplicate_DoesNotAllocate()
        {
            var metrics = new HistoryMetrics();
            var cache = new HistoryMemoryCache(1_073_741_824, shardCount: 64, metrics);
            const string messageId = "<alloc@test.local>";
            Span<byte> digest = stackalloc byte[32];
            Assert.That(HistoryKeyEncoder.TryComputeDigest(messageId, digest), Is.True);
            var key = new DigestKey(digest);
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            cache.InsertOrUpdate(in key, now + 3600);
            for (int i = 0; i < 10_000; i++)
            {
                _ = cache.TryGetDuplicate(in key, now);
            }

            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                _ = cache.TryGetDuplicate(in key, now);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies fake history duplicate path used in golden tests.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task FakeHistoryDatabase_Duplicate_ReturnsDuplicate()
        {
            var fake = new FakeHistoryDatabase();
            fake.SeedDuplicate("<dup@test.local>");
            HistoryCheckResult result = await fake.CheckAsync("<dup@test.local>", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(HistoryCheckResult.Duplicate));
        }
    }
}
