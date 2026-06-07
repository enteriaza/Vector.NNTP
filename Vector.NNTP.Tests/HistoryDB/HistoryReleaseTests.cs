// <copyright file="HistoryReleaseTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;

namespace Vector.NNTP.Tests.HistoryDB;

/// <summary>
/// Tests for full-tier history release helpers (Rocks delete and persist tombstones).
/// </summary>
[TestFixture]
public sealed class HistoryReleaseTests
{
    /// <summary>
    /// Verifies <see cref="RocksHistoryStore.DeleteByDigest"/> removes paired column-family rows.
    /// </summary>
    [Test]
    public void DeleteByDigest_RemovesDigestAndExpirationRows()
    {
        string dir = Path.Combine(Path.GetTempPath(), "historydb-delete-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new HistoryDbOptions { DbDir = dir, RememberDays = 2 });
            var metrics = new HistoryMetrics();
            using var rocks = new RocksHistoryStore(options, metrics, NullLogger<RocksHistoryStore>.Instance);
            byte[] digest = new byte[32];
            digest.AsSpan().Fill(0xCD);
            ulong exp = (ulong)DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
            rocks.PutReservation(digest, exp);
            Assert.That(rocks.GetDigestExpiration(digest), Is.EqualTo(exp));

            Assert.That(rocks.DeleteByDigest(digest), Is.True);
            Assert.That(rocks.GetDigestExpiration(digest), Is.Null);
            Assert.That(rocks.CountExpirationKeys(), Is.EqualTo(0));
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
    /// Verifies tombstoned persist items are skipped by <see cref="HistoryRocksPersistPump"/>.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PersistPump_TombstonedItem_IsNotWrittenToRocks()
    {
        string dir = Path.Combine(Path.GetTempPath(), "historydb-tombstone-" + Guid.NewGuid().ToString("N"));
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
            var tombstones = new HistoryReleaseTombstoneSet();
            var pump = new HistoryRocksPersistPump(
                rocks,
                tombstones,
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
                rocks,
                tombstones,
                lifetime,
                NullLogger<HistoryDatabaseService>.Instance);

            history.SetOperational();
            byte[] digest = new byte[32];
            digest.AsSpan().Fill(0xAB);
            tombstones.Add(new DigestKey(digest));
            ulong exp = (ulong)DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
            Assert.That(history.TryEnqueuePersist(digest, exp), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            Assert.That(rocks.GetDigestExpiration(digest), Is.Null);
            Assert.That(rocks.CountExpirationKeys(), Is.EqualTo(0));
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
    /// Minimal host lifetime for tests.
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
    /// Redis accessor that fails if invoked.
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
