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
        private readonly IRedisConnectionAccessor _redis;
        private readonly RedisCoordinationKeys _keys;
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

        /// <inheritdoc />
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
                LogDebugSessionCountChanged(accountKey, count);
                return count;
            }

            return 0;
        }
    }
}
