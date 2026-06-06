// <copyright file="HistoryDatabaseServiceAllocationTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Memory;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;
using Vector.NNTP.Session.Redis.Coordination;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Verifies zero heap allocation on operational memory-hit <see cref="HistoryDatabaseService.CheckAsync"/>.
    /// </summary>
    [TestFixture]
    public sealed class HistoryDatabaseServiceAllocationTests
    {
        private const string MessageId = "<memory-hit-zero-alloc@test.local>";

        /// <summary>
        /// Repeated memory-hit CHECK does not allocate after warmup.
        /// </summary>
        [Test]
        public void MemoryHit_CheckAsync_DoesNotAllocate()
        {
            var metrics = new HistoryMetrics();
            var memory = new HistoryMemoryCache(1_073_741_824, metrics);
            Span<byte> digest = stackalloc byte[HistoryKeyEncoder.DigestLength];
            Assert.That(HistoryKeyEncoder.TryComputeDigest(MessageId, digest), Is.True);
            var digestKey = new DigestKey(digest);
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            memory.InsertOrUpdate(in digestKey, now + 86_400);

            var options = Options.Create(new HistoryDbOptions { KeyPrefix = "test:", RememberDays = 2, QueueCapacity = 1024, DbDir = Path.GetTempPath() });
            var redis = new HistoryRedisStore(
                options,
                new UnreachableRedisAccessor(),
                metrics,
                NullLogger<HistoryRedisStore>.Instance);
            string rocksDir = Path.Combine(Path.GetTempPath(), "historydb-alloc-" + Guid.NewGuid().ToString("N"));
            var rocksOptions = Options.Create(new HistoryDbOptions { DbDir = rocksDir, RememberDays = 2, QueueCapacity = 1024 });
            using var rocks = new RocksHistoryStore(rocksOptions, metrics, NullLogger<RocksHistoryStore>.Instance);
            var pump = new HistoryRocksPersistPump(
                rocks,
                metrics,
                rocksOptions,
                NullLogger<HistoryRocksPersistPump>.Instance);
            var service = new HistoryDatabaseService(
                rocksOptions,
                memory,
                redis,
                metrics,
                pump,
                new NullHostApplicationLifetime(),
                NullLogger<HistoryDatabaseService>.Instance);
            service.SetOperational();

            for (int i = 0; i < 10_000; i++)
            {
                CompleteCheck(service.CheckAsync(MessageId, CancellationToken.None));
            }

            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            long before = GC.GetAllocatedBytesForCurrentThread();
            HistoryCheckResult last = default;
            for (int i = 0; i < 10_000; i++)
            {
                ValueTask<HistoryCheckResult> pending = service.CheckAsync(MessageId, CancellationToken.None);
                last = pending.Result;
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
            Assert.That(last, Is.EqualTo(HistoryCheckResult.Duplicate));
        }

        /// <summary>
        /// Completes a synchronously finished <see cref="ValueTask{T}"/> without awaiting.
        /// </summary>
        /// <param name="pending">Pending CHECK task.</param>
        /// <returns>CHECK result.</returns>
        private static HistoryCheckResult CompleteCheck(ValueTask<HistoryCheckResult> pending) => pending.Result;

        /// <summary>
        /// No-op host lifetime for unit tests.
        /// </summary>
        private sealed class NullHostApplicationLifetime : IHostApplicationLifetime
        {
            /// <inheritdoc />
            public CancellationToken ApplicationStarted { get; } = CancellationToken.None;

            /// <inheritdoc />
            public CancellationToken ApplicationStopping { get; } = CancellationToken.None;

            /// <inheritdoc />
            public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

            /// <inheritdoc />
            public void StopApplication()
            {
            }
        }

        /// <summary>
        /// Redis accessor that fails if the hot path incorrectly calls Redis.
        /// </summary>
        private sealed class UnreachableRedisAccessor : IRedisConnectionAccessor
        {
            /// <inheritdoc />
            public IDatabase GetDatabase() => throw new InvalidOperationException("Redis must not be called on memory-hit CHECK.");

            /// <inheritdoc />
            public void SignalScaleUp()
            {
            }
        }
    }
}
