// <copyright file="HistoryPersistPipelineTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Verifies the record-path persist queue drains into RocksDB via <see cref="HistoryRocksPersistPump"/>.
    /// </summary>
    [TestFixture]
    public sealed class HistoryPersistPipelineTests
    {
        /// <summary>
        /// Enqueued persist items are written to Rocks after <see cref="HistoryDatabaseService.SetOperational"/>.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task PersistPump_WritesReservationToRocks()
        {
            string dir = Path.Combine(Path.GetTempPath(), "historydb-persist-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = Options.Create(new HistoryDbOptions
                {
                    DbDir = dir,
                    RememberDays = 2,
                    QueueCapacity = 1024,
                });
                var metrics = new HistoryMetrics();
                using var rocks = new RocksHistoryStore(options, metrics, NullLogger<RocksHistoryStore>.Instance);
                var pump = new HistoryRocksPersistPump(
                    rocks,
                    metrics,
                    options,
                    NullLogger<HistoryRocksPersistPump>.Instance);
                var lifetime = new TestHostApplicationLifetime();
                var memory = new Vector.NNTP.HistoryDB.Memory.HistoryMemoryCache(1_073_741_824, shardCount: 64, metrics);
                var redis = new HistoryRedisStore(
                    options,
                    new UnreachableRedisAccessor(),
                    metrics,
                    NullLogger<HistoryRedisStore>.Instance);
                var history = new HistoryDatabaseService(
                    options,
                    memory,
                    redis,
                    metrics,
                    pump,
                    lifetime,
                    NullLogger<HistoryDatabaseService>.Instance);

                history.SetOperational();
                byte[] digest = new byte[32];
                digest.AsSpan().Fill(0xAB);
                ulong exp = (ulong)DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
                Assert.That(history.TryEnqueuePersist(digest, exp), Is.True);

                for (int i = 0; i < 50 && rocks.GetDigestExpiration(digest) is null; i++)
                {
                    await Task.Delay(20).ConfigureAwait(false);
                }

                Assert.That(rocks.GetDigestExpiration(digest), Is.EqualTo(exp));
                Assert.That(rocks.CountExpirationKeys(), Is.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        /// <summary>
        /// No-op host lifetime for unit tests.
        /// </summary>
        private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
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
        /// Redis accessor that fails if CHECK incorrectly calls Redis in this test.
        /// </summary>
        private sealed class UnreachableRedisAccessor : Vector.NNTP.Session.Redis.Coordination.IRedisConnectionAccessor
        {
            /// <inheritdoc />
            public StackExchange.Redis.IDatabase GetDatabase() =>
                throw new InvalidOperationException("Redis must not be used in this test.");

            /// <inheritdoc />
            public void SignalScaleUp()
            {
            }
        }
    }
}
