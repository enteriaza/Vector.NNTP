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

        /// <summary>
        /// Refreshes the Redis lease for the given session.
        /// </summary>
        /// <param name="accountKey">The account key of the session.</param>
        /// <param name="sessionId">The ID of the session.</param>
        /// <param name="ipText">The IP text of the session.</param>
        /// <param name="ttlSeconds">The TTL of the session in seconds.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when the heartbeat is completed.</returns>
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
