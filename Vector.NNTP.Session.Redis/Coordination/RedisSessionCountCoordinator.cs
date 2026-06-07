// <copyright file="RedisSessionCountCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using System.Globalization;
using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Reads cluster-wide authenticated session counts from Redis.
    /// </summary>
    public sealed partial class RedisSessionCountCoordinator : INntpSessionCountCoordinator
    {
        /// <summary>
        /// Round-robin Redis accessor for session counter reads.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis;

        /// <summary>
        /// Key builder for per-account session counter keys.
        /// </summary>
        private readonly RedisCoordinationKeys _keys;

        /// <summary>
        /// Logger for session count debug events.
        /// </summary>
        private readonly ILogger<RedisSessionCountCoordinator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisSessionCountCoordinator"/> class.
        /// </summary>
        /// <param name="redis">Redis connection accessor.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="logger">Logger.</param>
        public RedisSessionCountCoordinator(
            IRedisConnectionAccessor redis,
            IOptions<NntpSessionCoordinationOptions> options,
            ILogger<RedisSessionCountCoordinator> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            ArgumentNullException.ThrowIfNull(options);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _keys = new RedisCoordinationKeys(options.Value.KeyPrefix);
        }

        /// <summary>
        /// Reads the cluster-wide authenticated session counter for the normalized account key derived from <paramref name="username"/>.
        /// </summary>
        /// <param name="username">NNTP username used to compute the account key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Live session count from Redis, or zero when the counter key is absent or unparsable.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <exception cref="Exceptions.RedisUnavailableException">Thrown when the pool has no live multiplexers.</exception>
        public async Task<long> GetSessionCountAsync(string username, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);
            cancellationToken.ThrowIfCancellationRequested();
            string accountKey = AccountKeyNormalizer.ComputeAccountKey(username);
            IDatabase db = _redis.GetDatabase();
            RedisValue value = await db.StringGetAsync(_keys.Sessions(accountKey)).ConfigureAwait(false);
            if (!value.HasValue)
            {
                return 0;
            }

            if (long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
            {
                LogDebugSessionCountChanged(_logger, accountKey, count);
                return count;
            }

            return 0;
        }
    }
}
