// <copyright file="RedisNodeSessionRegistryTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Vector.NNTP.Session.Coordination;
using Vector.NNTP.Session.Database;
using Vector.NNTP.Session.Policy;
using Vector.NNTP.Session.Redis.Configuration;
using Vector.NNTP.Session.Redis.Coordination;

namespace Vector.NNTP.Tests.Session.Redis
{
    /// <summary>
    /// Integration tests for node-scoped Redis session registry (requires a live Redis instance).
    /// </summary>
    [TestFixture]
    [Category("Redis")]
    public sealed class RedisNodeSessionRegistryTests
    {
        private const string TestNode = "node-lifecycle-test";
        private const string KeyPrefix = "test:node-lifecycle:";

        private static bool _redisAvailable;
        private static string _skipReason = "Redis not reachable on localhost:6379.";

        private ConnectionMultiplexer? _multiplexer;
        private DirectRedisAccessor? _accessor;
        private RedisCoordinationKeys _keys;
        private RedisSessionCoordinator? _sessionCoordinator;
        private RedisTransitPeerCoordinator? _transitCoordinator;
        private RedisNodeSessionRegistry? _registry;

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
        /// Builds coordinators against an isolated key prefix.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            if (!_redisAvailable)
            {
                return;
            }

            var options = Options.Create(new NntpSessionCoordinationOptions
            {
                Hosts = ["localhost"],
                Port = 6379,
                KeyPrefix = KeyPrefix,
                HeartbeatIntervalSeconds = 1,
                TtlMinimumSeconds = 2,
            });
            _multiplexer = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
            _accessor = new DirectRedisAccessor(_multiplexer);
            _keys = new RedisCoordinationKeys(KeyPrefix);
            var sessionDatabase = new InMemorySessionDatabase();
            _sessionCoordinator = new RedisSessionCoordinator(
                _accessor,
                new RedisSessionReconciliationCoordinator(
                    _accessor,
                    sessionDatabase,
                    options,
                    NullLogger<RedisSessionReconciliationCoordinator>.Instance),
                options,
                NullLogger<RedisSessionCoordinator>.Instance);
            _transitCoordinator = new RedisTransitPeerCoordinator(
                _accessor,
                options,
                NullLogger<RedisTransitPeerCoordinator>.Instance);
            _registry = new RedisNodeSessionRegistry(
                _accessor,
                options,
                _sessionCoordinator,
                _transitCoordinator,
                NullLogger<RedisNodeSessionRegistry>.Instance);
        }

        /// <summary>
        /// Deletes test keys after each case.
        /// </summary>
        /// <returns>A task that completes when cleanup finishes.</returns>
        [TearDown]
        public async Task TearDownAsync()
        {
            if (_multiplexer is null)
            {
                return;
            }

            IDatabase db = _multiplexer.GetDatabase();
            foreach (EndPoint endpoint in _multiplexer.GetEndPoints())
            {
                IServer server = _multiplexer.GetServer(endpoint);
                await foreach (RedisKey key in server.KeysAsync(pattern: KeyPrefix + "*").ConfigureAwait(false))
                {
                    await db.KeyDeleteAsync(key).ConfigureAwait(false);
                }
            }

            await _multiplexer.CloseAsync().ConfigureAwait(false);
            _multiplexer.Dispose();
        }

        /// <summary>
        /// Mandatory crash-path: acquire sets HASH + node SET + TTL; without refresh both expire.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task Acquire_WithoutRefresh_TtlExpires_RemovesMetaAndNodeIndex()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            NntpSessionPolicy policy = NntpSessionPolicyFactory.Create(
                new NntpAccountLimits("user", 'R', 0, 0, 1, 1, string.Empty),
                allowPosting: true,
                normalizer);
            string sessionId = Guid.NewGuid().ToString("N");
            const int ttlSeconds = 2;
            NntpSessionAdmissionResult admit = await _sessionCoordinator!.TryAdmitAsync(
                policy,
                sessionId,
                "127.0.0.1",
                TestNode,
                ttlSeconds,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(admit, Is.EqualTo(NntpSessionAdmissionResult.Success));

            IDatabase db = _multiplexer!.GetDatabase();
            RedisKey metaKey = _keys.SessionMeta(sessionId);
            RedisKey nodeSetKey = _keys.NodeSessions(TestNode);
            Assert.That(await db.KeyExistsAsync(metaKey).ConfigureAwait(false), Is.True);
            Assert.That(await db.SetContainsAsync(nodeSetKey, sessionId).ConfigureAwait(false), Is.True);

            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(await db.KeyExistsAsync(metaKey).ConfigureAwait(false), Is.False);
            Assert.That(await db.SetContainsAsync(nodeSetKey, sessionId).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Verifies refresh advances leaseUpdated (milliseconds).
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task Heartbeat_AdvancesLeaseUpdatedMs()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            NntpSessionPolicy policy = NntpSessionPolicyFactory.Create(
                new NntpAccountLimits("user", 'R', 0, 0, 1, 1, string.Empty),
                allowPosting: true,
                normalizer);
            string sessionId = Guid.NewGuid().ToString("N");
            const int ttlSeconds = 60;
            _ = await _sessionCoordinator!.TryAdmitAsync(
                policy,
                sessionId,
                "127.0.0.1",
                TestNode,
                ttlSeconds,
                CancellationToken.None).ConfigureAwait(false);

            IDatabase db = _multiplexer!.GetDatabase();
            RedisValue createdBefore = await db.HashGetAsync(_keys.SessionMeta(sessionId), NodeSessionRegistryFields.LeaseUpdated)
                .ConfigureAwait(false);
            await Task.Delay(50).ConfigureAwait(false);

            var refresher = new RedisSessionLeaseRefresher(
                _accessor!,
                Options.Create(new NntpSessionCoordinationOptions { Hosts = ["localhost"], Port = 6379, KeyPrefix = KeyPrefix }),
                NullLogger<RedisSessionLeaseRefresher>.Instance);
            await refresher.HeartbeatAsync(
                policy.AccountKey,
                sessionId,
                "127.0.0.1",
                TestNode,
                ttlSeconds,
                CancellationToken.None).ConfigureAwait(false);

            RedisValue createdAfter = await db.HashGetAsync(_keys.SessionMeta(sessionId), NodeSessionRegistryFields.LeaseUpdated)
                .ConfigureAwait(false);
            Assert.That(long.Parse(createdAfter.ToString()!), Is.GreaterThan(long.Parse(createdBefore.ToString()!)));
        }

        /// <summary>
        /// Verifies PurgeNode clears more than one batch of indexed sessions.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task PurgeNode_ProgressLoop_ClearsLargeNodeIndex()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            IDatabase db = _multiplexer!.GetDatabase();
            RedisKey nodeSet = _keys.NodeSessions(TestNode);
            for (int i = 0; i < 550; i++)
            {
                string sessionId = "orphan-" + i;
                RedisKey metaKey = _keys.SessionMeta(sessionId);
                await db.HashSetAsync(
                    metaKey,
                    [
                        new HashEntry(NodeSessionRegistryFields.Node, TestNode),
                        new HashEntry(NodeSessionRegistryFields.Kind, NodeSessionRegistryFields.KindTransit),
                        new HashEntry(NodeSessionRegistryFields.PeerId, "peer-purge"),
                        new HashEntry(NodeSessionRegistryFields.Created, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                        new HashEntry(NodeSessionRegistryFields.LeaseUpdated, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                    ]).ConfigureAwait(false);
                _ = await db.SetAddAsync(nodeSet, sessionId).ConfigureAwait(false);
            }

            NodeSessionPurgeResult result = await _registry!.PurgeNodeAsync(TestNode, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.TransitLeasesPurged, Is.GreaterThan(500));
            Assert.That(await db.KeyExistsAsync(nodeSet).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Verifies hitting the iteration cap logs a warning with iterations and remaining sessions.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task PurgeNode_HitIterationLimit_LogsWarningWithRemainingSessions()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            var logCollector = new LogCollector();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logCollector));
            ILogger<RedisNodeSessionRegistry> logger = loggerFactory.CreateLogger<RedisNodeSessionRegistry>();
            RedisNodeSessionRegistry registry = CreateRegistryWithBounds(maxIterations: 2, batchSize: 1, logger);
            IDatabase db = _multiplexer!.GetDatabase();
            RedisKey nodeSet = _keys.NodeSessions(TestNode);
            for (int i = 0; i < 5; i++)
            {
                string sessionId = "limit-" + i;
                await db.HashSetAsync(
                    _keys.SessionMeta(sessionId),
                    [
                        new HashEntry(NodeSessionRegistryFields.Node, TestNode),
                        new HashEntry(NodeSessionRegistryFields.Kind, NodeSessionRegistryFields.KindTransit),
                        new HashEntry(NodeSessionRegistryFields.PeerId, "peer-purge"),
                        new HashEntry(NodeSessionRegistryFields.Created, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                        new HashEntry(NodeSessionRegistryFields.LeaseUpdated, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                    ]).ConfigureAwait(false);
                _ = await db.SetAddAsync(nodeSet, sessionId).ConfigureAwait(false);
            }

            NodeSessionPurgeResult result = await registry.PurgeNodeAsync(TestNode, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.HitIterationLimit, Is.True);
            Assert.That(result.RemainingSessions, Is.GreaterThan(0));
            Assert.That(
                logCollector.Messages.Any(static m =>
                    m.Level == LogLevel.Warning &&
                    m.Text.Contains("iteration limit", StringComparison.OrdinalIgnoreCase) &&
                    m.Text.Contains("Iterations=", StringComparison.Ordinal) &&
                    m.Text.Contains("RemainingSessions=", StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// Verifies purge completes when the node index mutates during the progress loop.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task PurgeNode_ConcurrentSetMutation_StillCompletes()
        {
            if (!_redisAvailable)
            {
                Assert.Ignore(_skipReason);
            }

            IDatabase db = _multiplexer!.GetDatabase();
            RedisKey nodeSet = _keys.NodeSessions(TestNode);
            for (int i = 0; i < 20; i++)
            {
                string sessionId = "mutate-" + i;
                await SeedTransitOrphanAsync(db, sessionId).ConfigureAwait(false);
            }

            Task<NodeSessionPurgeResult> purgeTask = _registry!.PurgeNodeAsync(TestNode, CancellationToken.None).AsTask();
            for (int i = 20; i < 40; i++)
            {
                string sessionId = "mutate-" + i;
                await SeedTransitOrphanAsync(db, sessionId).ConfigureAwait(false);
            }

            NodeSessionPurgeResult result = await purgeTask.ConfigureAwait(false);
            Assert.That(result.TransitLeasesPurged, Is.GreaterThan(0));
            Assert.That(await db.KeyExistsAsync(nodeSet).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Seeds a transit orphan entry in Redis.
        /// </summary>
        /// <param name="db">Database.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <returns>A task that completes when seeded.</returns>
        private async Task SeedTransitOrphanAsync(IDatabase db, string sessionId)
        {
            await db.HashSetAsync(
                _keys.SessionMeta(sessionId),
                [
                    new HashEntry(NodeSessionRegistryFields.Node, TestNode),
                    new HashEntry(NodeSessionRegistryFields.Kind, NodeSessionRegistryFields.KindTransit),
                    new HashEntry(NodeSessionRegistryFields.PeerId, "peer-purge"),
                    new HashEntry(NodeSessionRegistryFields.Created, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                    new HashEntry(NodeSessionRegistryFields.LeaseUpdated, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                ]).ConfigureAwait(false);
            _ = await db.SetAddAsync(_keys.NodeSessions(TestNode), sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds a registry with custom purge bounds for limit tests.
        /// </summary>
        /// <param name="maxIterations">Maximum iterations.</param>
        /// <param name="batchSize">Batch size.</param>
        /// <param name="logger">Logger.</param>
        /// <returns>Configured registry.</returns>
        private RedisNodeSessionRegistry CreateRegistryWithBounds(int maxIterations, int batchSize, ILogger<RedisNodeSessionRegistry> logger)
        {
            var options = Options.Create(new NntpSessionCoordinationOptions
            {
                Hosts = ["localhost"],
                Port = 6379,
                KeyPrefix = KeyPrefix,
            });
            return new RedisNodeSessionRegistry(
                _accessor!,
                options,
                _sessionCoordinator!,
                _transitCoordinator!,
                logger,
                maxIterations,
                batchSize);
        }

        /// <summary>
        /// Routes StackExchange.Redis through a single test multiplexer.
        /// </summary>
        private sealed class DirectRedisAccessor : IRedisConnectionAccessor
        {
            private readonly IConnectionMultiplexer _multiplexer;

            /// <summary>
            /// Initializes a new instance of the <see cref="DirectRedisAccessor"/> class.
            /// </summary>
            /// <param name="multiplexer">Shared multiplexer.</param>
            public DirectRedisAccessor(IConnectionMultiplexer multiplexer)
            {
                _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
            }

            /// <inheritdoc />
            public IDatabase GetDatabase()
            {
                return _multiplexer.GetDatabase();
            }

            /// <inheritdoc />
            public void SignalScaleUp()
            {
            }
        }

        /// <summary>
        /// Captures log messages for assertions.
        /// </summary>
        private sealed class LogCollector : ILoggerProvider
        {
            /// <summary>
            /// Gets captured messages.
            /// </summary>
            public List<(LogLevel Level, string Text)> Messages { get; } = [];

            /// <inheritdoc />
            public ILogger CreateLogger(string categoryName)
            {
                return new CollectorLogger(this);
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }

            /// <summary>
            /// Logger that appends formatted messages.
            /// </summary>
            private sealed class CollectorLogger(LogCollector owner) : ILogger
            {
                /// <inheritdoc />
                public IDisposable? BeginScope<TState>(TState state)
                    where TState : notnull
                {
                    return null;
                }

                /// <inheritdoc />
                public bool IsEnabled(LogLevel logLevel)
                {
                    return logLevel >= LogLevel.Warning;
                }

                /// <inheritdoc />
                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    owner.Messages.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
