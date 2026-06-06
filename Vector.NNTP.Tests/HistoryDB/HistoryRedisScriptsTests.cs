// <copyright file="HistoryRedisScriptsTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Integration tests for <c>HISTORY_CHECK_V1</c> and <c>HISTORY_RECORD_V1</c> (requires localhost Redis).
    /// </summary>
    [TestFixture]
    [Category("Redis")]
    public sealed class HistoryRedisScriptsTests
    {
        private const string KeyPrefix = "test:historydb:lua:";

        private static bool _redisAvailable;
        private static string _skipReason = "Redis not reachable on localhost:6379.";

        private ConnectionMultiplexer? _multiplexer;
        private HistoryRedisStore? _store;

        /// <summary>
        /// Probes Redis once for the fixture.
        /// </summary>
        [OneTimeSetUp]
        public void ProbeRedis()
        {
            try
            {
                using ConnectionMultiplexer mux = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false,connectTimeout=2000");
                _ = mux.GetDatabase().Ping();
                _redisAvailable = true;
            }
            catch (Exception ex)
            {
                _redisAvailable = false;
                _skipReason = ex.Message;
            }
        }

        /// <summary>
        /// Creates a store against an isolated key prefix.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            if (!_redisAvailable)
            {
                return;
            }

            this._multiplexer = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
            var accessor = new HistoryRedisTestAccessor(this._multiplexer);
            var options = Options.Create(new HistoryDbOptions { KeyPrefix = KeyPrefix, RememberDays = 2 });
            this._store = new HistoryRedisStore(
                options,
                accessor,
                new HistoryMetrics(),
                NullLogger<HistoryRedisStore>.Instance);
        }

        /// <summary>
        /// Disposes the multiplexer.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            this._multiplexer?.Dispose();
            this._multiplexer = null;
            this._store = null;
        }

        /// <summary>
        /// Fresh key probe returns wanted (0) without writing.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task CheckProbe_Fresh_ReturnsWanted()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            byte[] digest = CreateUniqueDigest(0x11);
            await this.CleanupKeyAsync(digest).ConfigureAwait(false);
            ulong now = NowEpoch();
            int code = await this._store!.CheckProbeAsync(digest, now, CancellationToken.None).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(0));
        }

        /// <summary>
        /// Record then probe returns duplicate (1) without second record.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task CheckProbe_AfterRecord_ReturnsDuplicate()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            byte[] digest = CreateUniqueDigest(0x12);
            await this.CleanupKeyAsync(digest).ConfigureAwait(false);
            ulong now = NowEpoch();
            int record = await this._store!.TryRecordAsync(digest, now, now + 3600, 3600, CancellationToken.None)
                .ConfigureAwait(false);
            int probe = await this._store.CheckProbeAsync(digest, now, CancellationToken.None).ConfigureAwait(false);
            Assert.That(record, Is.EqualTo(0));
            Assert.That(probe, Is.EqualTo(1));
        }

        /// <summary>
        /// Fresh key record returns recorded (0).
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task RecordFresh_ReturnsRecorded()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            byte[] digest = CreateUniqueDigest(0x21);
            await this.CleanupKeyAsync(digest).ConfigureAwait(false);
            ulong now = NowEpoch();
            int code = await this._store!.TryRecordAsync(digest, now, now + 3600, 3600, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(0));
        }

        /// <summary>
        /// Unexpired key record returns duplicate (1).
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task RecordTwice_SecondReturnsDuplicate()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            byte[] digest = CreateUniqueDigest(0x22);
            await this.CleanupKeyAsync(digest).ConfigureAwait(false);
            ulong now = NowEpoch();
            int first = await this._store!.TryRecordAsync(digest, now, now + 3600, 3600, CancellationToken.None)
                .ConfigureAwait(false);
            int second = await this._store.TryRecordAsync(digest, now, now + 3600, 3600, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(first, Is.EqualTo(0));
            Assert.That(second, Is.EqualTo(1));
        }

        /// <summary>
        /// Expired value is removed and record succeeds (0).
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task RecordExpiredExisting_AllowsRecord_ReturnsRecorded()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            byte[] digest = CreateUniqueDigest(0x23);
            await this.CleanupKeyAsync(digest).ConfigureAwait(false);
            ulong now = NowEpoch();
            IDatabase db = this._multiplexer!.GetDatabase();
            RedisKey key = this._store!.BuildHistoryKey(digest);
            await db.StringSetAsync(key, (now - 60).ToString()).ConfigureAwait(false);
            int code = await this._store.TryRecordAsync(digest, now, now + 3600, 3600, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(0));
        }

        /// <summary>
        /// Probe on fresh key does not create a Redis key.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task CheckProbe_Fresh_DoesNotSetKey()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            byte[] digest = CreateUniqueDigest(0x13);
            await this.CleanupKeyAsync(digest).ConfigureAwait(false);
            ulong now = NowEpoch();
            RedisKey key = this._store!.BuildHistoryKey(digest);
            _ = await this._store.CheckProbeAsync(digest, now, CancellationToken.None).ConfigureAwait(false);
            Assert.That(await this._multiplexer!.GetDatabase().KeyExistsAsync(key).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Returns current UTC epoch seconds for script arguments.
        /// </summary>
        /// <returns>Unix epoch seconds.</returns>
        private static ulong NowEpoch() => (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>
        /// Builds a unique 32-byte digest for isolated Redis keys.
        /// </summary>
        /// <param name="seed">Seed byte mixed into the digest.</param>
        /// <returns>32-byte digest.</returns>
        private static byte[] CreateUniqueDigest(byte seed)
        {
            byte[] digest = new byte[32];
            digest[0] = seed;
            _ = Guid.NewGuid().TryWriteBytes(digest.AsSpan(1, 16));
            return digest;
        }

        /// <summary>
        /// Deletes a history key before a scenario runs.
        /// </summary>
        /// <param name="digest">Digest bytes.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous cleanup.</returns>
        private async Task CleanupKeyAsync(byte[] digest)
        {
            RedisKey key = this._store!.BuildHistoryKey(digest);
            await this._multiplexer!.GetDatabase().KeyDeleteAsync(key).ConfigureAwait(false);
        }
    }
}
