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
        /// Redis connection accessor.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis;

        /// <summary>
        /// Redis coordination keys.
        /// </summary>
        private readonly RedisCoordinationKeys _keys;

        /// <summary>
        /// Logger.
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
        /// Ensures the account quota key exists (idempotent initialize).
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="byteLimit">Initial quota bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when the key was created or lowered to policy.</returns>
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
        /// Decrements remaining quota by command bytes.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="commandBytes">Bytes to decrement.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Remaining bytes after decrement.</returns>
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
