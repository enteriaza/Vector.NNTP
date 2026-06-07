// <copyright file="RedisBlockQuotaCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Redis-backed byte quota for byte-limited accounts.
    /// </summary>
    public sealed partial class RedisBlockQuotaCoordinator : INntpBlockQuotaCoordinator
    {
        /// <summary>
        /// Round-robin Redis accessor for quota initialize and decrement scripts.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis;

        /// <summary>
        /// Key builder for per-account byte quota keys.
        /// </summary>
        private readonly RedisCoordinationKeys _keys;

        /// <summary>
        /// Logger for quota decrement debug events.
        /// </summary>
        private readonly ILogger<RedisBlockQuotaCoordinator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisBlockQuotaCoordinator"/> class.
        /// </summary>
        /// <param name="redis">Redis connection accessor.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="logger">Logger.</param>
        public RedisBlockQuotaCoordinator(
            IRedisConnectionAccessor redis,
            IOptions<NntpSessionCoordinationOptions> options,
            ILogger<RedisBlockQuotaCoordinator> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            ArgumentNullException.ThrowIfNull(options);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _keys = new RedisCoordinationKeys(options.Value.KeyPrefix);
        }

        /// <summary>
        /// Ensures the account quota key exists without overwriting an existing value.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="byteLimit">Initial quota bytes to store when the key is absent.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when the key was created; <see langword="false"/> when it already exists or <paramref name="byteLimit"/> is not positive.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountKey"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <exception cref="Exceptions.RedisUnavailableException">Thrown when the pool has no live multiplexers.</exception>
        public async ValueTask<bool> TryInitializeQuotaAsync(string accountKey, long byteLimit, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            cancellationToken.ThrowIfCancellationRequested();
            if (byteLimit <= 0)
            {
                return false;
            }

            IDatabase db = _redis.GetDatabase();
            RedisKey quotaKey = _keys.Quota(accountKey);
            return await db.StringSetAsync(quotaKey, byteLimit, when: When.NotExists).ConfigureAwait(false);
        }

        /// <summary>
        /// Atomically decrements remaining byte quota for an account using a Lua script.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="commandBytes">Bytes to subtract from the remaining quota.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Remaining bytes after decrement, or <see cref="long.MaxValue"/> when <paramref name="commandBytes"/> is not positive.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="accountKey"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <exception cref="Exceptions.RedisUnavailableException">Thrown when the pool has no live multiplexers.</exception>
        public async ValueTask<long> DecrementAsync(string accountKey, long commandBytes, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            cancellationToken.ThrowIfCancellationRequested();
            if (commandBytes <= 0)
            {
                return long.MaxValue;
            }

            IDatabase db = _redis.GetDatabase();
            RedisKey[] keys = [_keys.Quota(accountKey)];
            RedisValue[] argv = [commandBytes];
            RedisResult result = await db.ScriptEvaluateAsync(RedisLuaScripts.QuotaDecrV1, keys, argv).ConfigureAwait(false);
            long remaining = (long)result;
            LogDebugQuotaDecremented(accountKey, commandBytes, remaining);
            return remaining;
        }
    }
}
