// <copyright file="RedisTransitPeerCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics;
using StackExchange.Redis;
using Vector.NNTP.Session.Coordination;
using Vector.NNTP.Session.Redis.Configuration;
using Vector.NNTP.Session.Utilities;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Redis ZSET-backed <see cref="INntpTransitPeerCoordinator"/> for cluster-wide transit peer connection caps.
    /// </summary>
    public sealed partial class RedisTransitPeerCoordinator : INntpTransitPeerCoordinator
    {
        private readonly IRedisConnectionAccessor _redis;
        private readonly RedisCoordinationKeys _keys;
        private readonly int _slowThresholdMs;
        private readonly int _transitPeerLeaseSeconds;
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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
