// <copyright file="RedisSessionLeaseRefresher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using System.Diagnostics;
using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Redis implementation of <see cref="IRedisSessionLeaseRefresher"/>.
    /// </summary>
    public sealed partial class RedisSessionLeaseRefresher : IRedisSessionLeaseRefresher
    {
        /// <summary>
        /// Logger for lease refresh trace, failures, and slow-call warnings.
        /// </summary>
        private readonly ILogger<RedisSessionLeaseRefresher> _logger;

        /// <summary>
        /// Round-robin Redis accessor for heartbeat script evaluation.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis;

        /// <summary>
        /// Key builder for session anchors, IP sets, and node registry metadata.
        /// </summary>
        private readonly RedisCoordinationKeys _keys;

        /// <summary>
        /// The threshold in milliseconds for a slow Redis operation.
        /// </summary>
        /// <remarks>
        /// If the elapsed time is greater than or equal to this threshold, a warning will be logged.
        /// </remarks>
        private readonly int _slowThresholdMs;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisSessionLeaseRefresher"/> class.
        /// </summary>
        /// <param name="redis">Redis connection accessor.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="logger">Logger.</param>
        public RedisSessionLeaseRefresher(
            IRedisConnectionAccessor redis,
            IOptions<NntpSessionCoordinationOptions> options,
            ILogger<RedisSessionLeaseRefresher> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            ArgumentNullException.ThrowIfNull(options);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _keys = new RedisCoordinationKeys(options.Value.KeyPrefix);
            _slowThresholdMs = Math.Max(0, options.Value.SlowRedisCallThresholdMs);
        }

        /// <summary>
        /// Extends lease TTL for a session anchor, its IP set, account counters, and node session registry metadata.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="ipText">Client IP text.</param>
        /// <param name="nodeName">Stable cluster node identity.</param>
        /// <param name="ttlSeconds">Lease TTL seconds applied to anchor and related keys.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the heartbeat script finishes.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountKey"/>, <paramref name="sessionId"/>, <paramref name="ipText"/>, or <paramref name="nodeName"/> is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ttlSeconds"/> is zero or negative.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <exception cref="Exception">Propagated when the Redis heartbeat script fails after logging.</exception>
        public async Task HeartbeatAsync(
            string accountKey,
            string sessionId,
            string ipText,
            string nodeName,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(ipText);
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ttlSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            int metaTtl = NntpSessionTtlCalculator.ComputeMetadataTtlSeconds(ttlSeconds);
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            RedisKey[] redisKeys =
            [
                _keys.SessionAnchor(accountKey, sessionId),
                _keys.IpSessions(accountKey, ipText),
                _keys.Sessions(accountKey),
                _keys.Ips(accountKey),
                _keys.SessionMeta(sessionId),
                _keys.NodeSessions(nodeName),
            ];
            RedisValue[] argv = [ttlSeconds, nowMs, metaTtl];
            IDatabase db = _redis.GetDatabase();
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                _ = await db.ScriptEvaluateAsync(RedisLuaScripts.SessionHeartbeatV2, redisKeys, argv).ConfigureAwait(false);
                LogTraceRedisLeaseRefreshed(_logger, sessionId, accountKey, ttlSeconds);
                double elapsedMs = sw.Elapsed.TotalMilliseconds;
                if (_slowThresholdMs > 0 && elapsedMs >= _slowThresholdMs)
                {
                    LogWarningRedisOperationSlow(_logger, "session-heartbeat", elapsedMs);
                    _redis.SignalScaleUp();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarningRedisLeaseRefreshFailed(_logger, ex, accountKey, sessionId);
                throw;
            }
        }
    }
}
