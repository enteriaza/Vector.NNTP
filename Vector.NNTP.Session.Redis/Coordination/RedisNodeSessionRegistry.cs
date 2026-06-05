// <copyright file="RedisNodeSessionRegistry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using StackExchange.Redis;
using Vector.NNTP.Session.Redis.Metrics;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Redis-backed node session registry purge using <c>node:{node}:sessions</c> and <c>session:{id}</c> metadata.
    /// </summary>
    public sealed partial class RedisNodeSessionRegistry : INodeSessionRegistry
    {
        private readonly IRedisConnectionAccessor _redis;
        private readonly RedisCoordinationKeys _keys;
        private readonly INntpSessionCoordinator _sessionCoordinator;
        private readonly INntpTransitPeerCoordinator _transitPeerCoordinator;
        private readonly ILogger<RedisNodeSessionRegistry> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisNodeSessionRegistry"/> class.
        /// </summary>
        /// <param name="redis">Redis accessor.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="sessionCoordinator">Auth admission coordinator.</param>
        /// <param name="transitPeerCoordinator">Transit peer coordinator.</param>
        /// <param name="logger">Logger.</param>
        public RedisNodeSessionRegistry(
            IRedisConnectionAccessor redis,
            IOptions<NntpSessionCoordinationOptions> options,
            INntpSessionCoordinator sessionCoordinator,
            INntpTransitPeerCoordinator transitPeerCoordinator,
            ILogger<RedisNodeSessionRegistry> logger)
            : this(redis, options, sessionCoordinator, transitPeerCoordinator, logger, 100_000, 500)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisNodeSessionRegistry"/> class with explicit purge bounds.
        /// </summary>
        /// <param name="redis">Redis accessor.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="sessionCoordinator">Auth admission coordinator.</param>
        /// <param name="transitPeerCoordinator">Transit peer coordinator.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="maxPurgeIterations">Maximum purge loop iterations.</param>
        /// <param name="purgeBatchSize">Batch size per purge iteration.</param>
        internal RedisNodeSessionRegistry(
            IRedisConnectionAccessor redis,
            IOptions<NntpSessionCoordinationOptions> options,
            INntpSessionCoordinator sessionCoordinator,
            INntpTransitPeerCoordinator transitPeerCoordinator,
            ILogger<RedisNodeSessionRegistry> logger,
            int maxPurgeIterations,
            int purgeBatchSize)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            ArgumentNullException.ThrowIfNull(options);
            _sessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
            _transitPeerCoordinator = transitPeerCoordinator ?? throw new ArgumentNullException(nameof(transitPeerCoordinator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _keys = new RedisCoordinationKeys(options.Value.KeyPrefix);
            MaxPurgeIterations = maxPurgeIterations;
            PurgeBatchSize = purgeBatchSize;
        }

        /// <inheritdoc />
        public int MaxPurgeIterations { get; }

        /// <inheritdoc />
        public int PurgeBatchSize { get; }

        /// <inheritdoc />
        public async ValueTask<NodeSessionPurgeResult> PurgeNodeAsync(string nodeName, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch sw = Stopwatch.StartNew();
            long authPurged = 0;
            long transitPurged = 0;
            int iteration = 0;
            bool hitLimit = false;
            IDatabase db = _redis.GetDatabase();
            RedisKey nodeSetKey = _keys.NodeSessions(nodeName);

            while (iteration < MaxPurgeIterations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RedisValue[] batch = await ScanBatchAsync(db, nodeSetKey, PurgeBatchSize).ConfigureAwait(false);
                if (batch.Length == 0)
                {
                    break;
                }

                long purgedThisRound = 0;
                foreach (RedisValue sessionIdValue in batch)
                {
                    string sessionId = sessionIdValue.ToString();
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        continue;
                    }

                    (bool released, bool isAuth) = await TryReleaseFromMetaAsync(
                        db,
                        sessionId,
                        nodeName,
                        cancellationToken).ConfigureAwait(false);
                    if (released)
                    {
                        purgedThisRound++;
                        if (isAuth)
                        {
                            authPurged++;
                        }
                        else
                        {
                            transitPurged++;
                        }
                    }
                }

                if (purgedThisRound == 0)
                {
                    break;
                }

                iteration++;
            }

            long remaining = await db.SetLengthAsync(nodeSetKey).ConfigureAwait(false);
            if (iteration >= MaxPurgeIterations && remaining > 0)
            {
                hitLimit = true;
                LogWarningPurgeIterationLimit(_logger, nodeName, iteration, remaining);
            }

            if (remaining == 0)
            {
                _ = await db.KeyDeleteAsync(nodeSetKey).ConfigureAwait(false);
            }

            double durationMs = sw.Elapsed.TotalMilliseconds;
            NodeSessionPurgeMetrics.RecordAuthPurged(nodeName, authPurged);
            NodeSessionPurgeMetrics.RecordTransitPurged(nodeName, transitPurged);
            NodeSessionPurgeMetrics.RecordDuration(nodeName, durationMs);
            return new NodeSessionPurgeResult(authPurged, transitPurged, durationMs, hitLimit, remaining);
        }

        /// <summary>Scans up to <paramref name="count"/> members from a SET using a fresh cursor.</summary>
        /// <param name="db">Redis database.</param>
        /// <param name="setKey">SET key.</param>
        /// <param name="count">Maximum members to return.</param>
        /// <returns>Member values.</returns>
        private static async Task<RedisValue[]> ScanBatchAsync(IDatabase db, RedisKey setKey, int count)
        {
            IAsyncEnumerable<RedisValue> scan = db.SetScanAsync(setKey, pageSize: count);
            List<RedisValue> batch = new(capacity: count);
            await foreach (RedisValue value in scan.ConfigureAwait(false))
            {
                batch.Add(value);
                if (batch.Count >= count)
                {
                    break;
                }
            }

            return [.. batch];
        }

        /// <summary>Releases coordination and registry keys for one session id.</summary>
        /// <param name="db">Redis database.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="purgeNodeName">Node identity used when metadata lacks a node field.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Whether release ran and whether kind was auth.</returns>
        private async Task<(bool Released, bool IsAuth)> TryReleaseFromMetaAsync(
            IDatabase db,
            string sessionId,
            string purgeNodeName,
            CancellationToken cancellationToken)
        {
            RedisKey metaKey = _keys.SessionMeta(sessionId);
            HashEntry[] entries = await db.HashGetAllAsync(metaKey).ConfigureAwait(false);
            if (entries.Length == 0)
            {
                return (false, false);
            }

            string? kind = null;
            string? accountKey = null;
            string? clientIp = null;
            string? peerId = null;
            string? node = null;
            foreach (HashEntry entry in entries)
            {
                string field = entry.Name.ToString();
                if (field == NodeSessionRegistryFields.Kind)
                {
                    kind = entry.Value.ToString();
                }
                else if (field == NodeSessionRegistryFields.AccountKey)
                {
                    accountKey = entry.Value.ToString();
                }
                else if (field == NodeSessionRegistryFields.ClientIp)
                {
                    clientIp = entry.Value.ToString();
                }
                else if (field == NodeSessionRegistryFields.PeerId)
                {
                    peerId = entry.Value.ToString();
                }
                else if (field == NodeSessionRegistryFields.Node)
                {
                    node = entry.Value.ToString();
                }
            }

            try
            {
                if (string.Equals(kind, NodeSessionRegistryFields.KindAuth, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(accountKey) &&
                    !string.IsNullOrEmpty(clientIp))
                {
                    NntpSessionPolicy policy = new(
                        username: "purge",
                        allowPosting: false,
                        accountType: NntpAccountType.RateLimited,
                        customerId: string.Empty,
                        rateBytesPerSecond: 0,
                        byteLimit: 0,
                        sessionLimit: 1,
                        srcIpLimit: 0,
                        accountKey: accountKey);
                    string releaseNode = node ?? purgeNodeName;
                    await _sessionCoordinator
                        .ReleaseAsync(policy, sessionId, clientIp, releaseNode, cancellationToken)
                        .ConfigureAwait(false);
                    return (true, true);
                }

                if (string.Equals(kind, NodeSessionRegistryFields.KindTransit, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(peerId))
                {
                    string releaseNode = node ?? purgeNodeName;
                    await _transitPeerCoordinator
                        .ReleaseAsync(peerId, sessionId, releaseNode, cancellationToken)
                        .ConfigureAwait(false);
                    return (true, false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarningReleaseDuringPurgeFailed(_logger, ex, sessionId, node ?? string.Empty);
            }

            _ = await db.KeyDeleteAsync(metaKey).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(node))
            {
                _ = await db.SetRemoveAsync(_keys.NodeSessions(node), sessionId).ConfigureAwait(false);
            }

            return (false, false);
        }
    }
}
