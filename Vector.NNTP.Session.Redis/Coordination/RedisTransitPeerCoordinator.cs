// <copyright file="RedisTransitPeerCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Redis ZSET-backed <see cref="INntpTransitPeerCoordinator"/> for cluster-wide transit peer connection caps.
    /// </summary>
    public sealed partial class RedisTransitPeerCoordinator : INntpTransitPeerCoordinator
    {
        /// <summary>
        /// Round-robin Redis accessor for ZSET coordination scripts.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis;

        /// <summary>
        /// Key builder for transit peer ZSETs and session metadata.
        /// </summary>
        private readonly RedisCoordinationKeys _keys;

        /// <summary>
        /// Elapsed milliseconds at or above which acquire is logged as slow and may signal pool scale-up.
        /// </summary>
        private readonly int _slowThresholdMs;

        /// <summary>
        /// Default stale-score cutoff derived from heartbeat interval and minimum TTL options.
        /// </summary>
        private readonly int _transitPeerLeaseSeconds;

        /// <summary>
        /// Logger for acquire, release, refresh failures, and slow Redis calls.
        /// </summary>
        private readonly ILogger<RedisTransitPeerCoordinator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisTransitPeerCoordinator"/> class.
        /// </summary>
        /// <param name="redis">Redis connection accessor.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="logger">Logger.</param>
        public RedisTransitPeerCoordinator(
            IRedisConnectionAccessor redis,
            IOptions<NntpSessionCoordinationOptions> options,
            ILogger<RedisTransitPeerCoordinator> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            ArgumentNullException.ThrowIfNull(options);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _keys = new RedisCoordinationKeys(options.Value.KeyPrefix);
            _slowThresholdMs = Math.Max(0, options.Value.SlowRedisCallThresholdMs);
            NntpSessionCoordinationOptions coordination = options.Value;
            _transitPeerLeaseSeconds = NntpSessionTtlCalculator.ComputeTransitPeerLeaseSeconds(
                coordination.HeartbeatIntervalSeconds,
                coordination.TtlMinimumSeconds);
        }

        /// <summary>
        /// Attempts to acquire a peer session slot in the cluster ZSET after purging stale members.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Globally unique session identifier.</param>
        /// <param name="maxConnections">Configured cap (0 = unlimited).</param>
        /// <param name="leaseSeconds">Stale score cutoff and lease extension window.</param>
        /// <param name="nodeName">Stable cluster node identity accepting the connection.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// <see cref="NntpTransitPeerAdmissionResult.Success"/> when a slot is acquired,
        /// <see cref="NntpTransitPeerAdmissionResult.AtCapacity"/> when the cap is reached, or
        /// <see cref="NntpTransitPeerAdmissionResult.BackendFailure"/> when Redis is unavailable.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="peerId"/>, <paramref name="sessionId"/>, or <paramref name="nodeName"/> is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="leaseSeconds"/> is zero or negative.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public async ValueTask<NntpTransitPeerAdmissionResult> TryAcquireAsync(
            string peerId,
            string sessionId,
            int maxConnections,
            int leaseSeconds,
            string nodeName,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long code = await EvaluateAcquireAsync(
                        peerId,
                        sessionId,
                        maxConnections,
                        leaseSeconds,
                        nodeName,
                        cancellationToken)
                    .ConfigureAwait(false);
                long count = await ReconcileZsetCountAsync(peerId, leaseSeconds, cancellationToken).ConfigureAwait(false);
                TransitPeerCapacityRegistry.UpdateCurrentCapacity(peerId, count);
                return code == 0
                    ? NntpTransitPeerAdmissionResult.Success
                    : NntpTransitPeerAdmissionResult.AtCapacity;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogWarningTransitPeerAcquireFailed(_logger, ex, peerId, sessionId);
                return NntpTransitPeerAdmissionResult.BackendFailure;
            }
        }

        /// <summary>
        /// Releases a previously acquired peer session slot and removes related metadata (idempotent).
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Session identifier used at acquire time.</param>
        /// <param name="nodeName">Stable cluster node identity that accepted the connection.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the release script finishes.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="peerId"/>, <paramref name="sessionId"/>, or <paramref name="nodeName"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <exception cref="Exception">Propagated when the Redis release script fails after logging.</exception>
        public async ValueTask ReleaseAsync(
            string peerId,
            string sessionId,
            string nodeName,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            cancellationToken.ThrowIfCancellationRequested();
            RedisKey[] redisKeys =
            [
                _keys.TransitPeerSessions(peerId),
                _keys.SessionMeta(sessionId),
                _keys.NodeSessions(nodeName),
            ];
            RedisValue[] argv = [sessionId];
            try
            {
                IDatabase db = _redis.GetDatabase();
                _ = await db.ScriptEvaluateAsync(RedisLuaScripts.TransitPeerReleaseV2, redisKeys, argv).ConfigureAwait(false);
                long count = await ReconcileZsetCountAsync(peerId, _transitPeerLeaseSeconds, cancellationToken)
                    .ConfigureAwait(false);
                TransitPeerCapacityRegistry.UpdateCurrentCapacity(peerId, count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogWarningTransitPeerReleaseFailed(_logger, ex, peerId, sessionId);
                throw;
            }
        }

        /// <summary>
        /// Refreshes the ZSET score and metadata TTL for a live transit peer session lease.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="nodeName">Stable cluster node identity that accepted the connection.</param>
        /// <param name="leaseSeconds">Lease window in seconds used for score recency and metadata TTL.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the refresh script finishes.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="peerId"/>, <paramref name="sessionId"/>, or <paramref name="nodeName"/> is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="leaseSeconds"/> is zero or negative.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <exception cref="Exception">Propagated when the Redis refresh script fails after logging.</exception>
        public async ValueTask RefreshLeaseAsync(
            string peerId,
            string sessionId,
            string nodeName,
            int leaseSeconds,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            int metaTtl = NntpSessionTtlCalculator.ComputeMetadataTtlSeconds(leaseSeconds);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            RedisKey[] redisKeys =
            [
                _keys.TransitPeerSessions(peerId),
                _keys.SessionMeta(sessionId),
                _keys.NodeSessions(nodeName),
            ];
            RedisValue[] argv = [now, sessionId, nowMs, metaTtl];
            try
            {
                IDatabase db = _redis.GetDatabase();
                _ = await db.ScriptEvaluateAsync(RedisLuaScripts.TransitPeerRefreshV2, redisKeys, argv).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogWarningTransitPeerRefreshFailed(_logger, ex, peerId, sessionId);
                throw;
            }
        }

        /// <summary>
        /// Purges stale ZSET members and returns the current live session count for metrics reconciliation.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Live session count after purge.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="peerId"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public async ValueTask<long> ReconcileCapacityAsync(string peerId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            return await ReconcileZsetCountAsync(peerId, _transitPeerLeaseSeconds, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Purges stale ZSET members and returns the live count.</summary>
        /// <param name="peerId">Peer identifier.</param>
        /// <param name="leaseSeconds">Lease window for stale cutoff.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Live session count.</returns>
        private async ValueTask<long> ReconcileZsetCountAsync(
            string peerId,
            int leaseSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RedisKey[] redisKeys = [_keys.TransitPeerSessions(peerId)];
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            RedisValue[] argv = [now, leaseSeconds];
            IDatabase db = _redis.GetDatabase();
            RedisResult result = await db.ScriptEvaluateAsync(RedisLuaScripts.TransitPeerReconcileV1, redisKeys, argv)
                .ConfigureAwait(false);
            return (long)result;
        }

        /// <summary>Runs the transit peer acquire Lua script.</summary>
        /// <param name="peerId">Peer identifier.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="maxConnections">Configured cap (0 = unlimited).</param>
        /// <param name="leaseSeconds">Lease seconds.</param>
        /// <param name="nodeName">Stable cluster node identity.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Lua return code.</returns>
        private async Task<long> EvaluateAcquireAsync(
            string peerId,
            string sessionId,
            int maxConnections,
            int leaseSeconds,
            string nodeName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int metaTtl = NntpSessionTtlCalculator.ComputeMetadataTtlSeconds(leaseSeconds);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            RedisKey[] redisKeys =
            [
                _keys.TransitPeerSessions(peerId),
                _keys.SessionMeta(sessionId),
                _keys.NodeSessions(nodeName),
            ];
            RedisValue[] argv = [now, leaseSeconds, maxConnections, sessionId, nodeName, peerId, nowMs, metaTtl];
            IDatabase db = _redis.GetDatabase();
            Stopwatch sw = Stopwatch.StartNew();
            RedisResult result = await db.ScriptEvaluateAsync(RedisLuaScripts.TransitPeerAcquireV2, redisKeys, argv)
                .ConfigureAwait(false);
            double elapsedMs = sw.Elapsed.TotalMilliseconds;
            if (_slowThresholdMs > 0 && elapsedMs >= _slowThresholdMs)
            {
                LogWarningRedisOperationSlow(_logger, "transit-peer-acquire", elapsedMs);
                _redis.SignalScaleUp();
            }

            return (long)result;
        }
    }
}
