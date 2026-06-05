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
        /// Logger.
        /// </summary>
        private readonly ILogger<RedisSessionLeaseRefresher> _logger;

        /// <summary>
        /// Redis connection accessor.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis;

        /// <summary>
        /// Redis coordination keys.
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

        /// <inheritdoc />
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
