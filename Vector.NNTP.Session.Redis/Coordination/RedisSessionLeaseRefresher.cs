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
        private readonly IRedisConnectionAccessor _redis;
        private readonly RedisCoordinationKeys _keys;
        private readonly ILogger<RedisSessionLeaseRefresher> _logger;
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
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(ipText);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ttlSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            RedisKey[] redisKeys =
            [
                _keys.SessionAnchor(accountKey, sessionId),
                _keys.IpSessions(accountKey, ipText),
                _keys.Sessions(accountKey),
                _keys.Ips(accountKey),
            ];
            RedisValue[] argv = [ttlSeconds];
            IDatabase db = _redis.GetDatabase();
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                _ = await db.ScriptEvaluateAsync(RedisLuaScripts.SessionHeartbeatV1, redisKeys, argv).ConfigureAwait(false);
                LogTraceRedisLeaseRefreshed(sessionId, accountKey, ttlSeconds);
                double elapsedMs = sw.Elapsed.TotalMilliseconds;
                if (_slowThresholdMs > 0 && elapsedMs >= _slowThresholdMs)
                {
                    LogWarningRedisOperationSlow("session-heartbeat", elapsedMs);
                    _redis.SignalScaleUp();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarningRedisLeaseRefreshFailed(accountKey, sessionId, ex);
                throw;
            }
        }
    }
}
