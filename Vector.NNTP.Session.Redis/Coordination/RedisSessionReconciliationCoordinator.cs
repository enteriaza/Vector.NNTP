// <copyright file="RedisSessionReconciliationCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Bounded SCAN/Lua reconciliation for session and IP counters.
    /// </summary>
    public sealed partial class RedisSessionReconciliationCoordinator : IRedisSessionReconciliationCoordinator
    {
        private readonly IRedisConnectionAccessor _redis;
        private readonly ISessionDatabase _sessionDatabase;
        private readonly RedisCoordinationKeys _keys;
        private readonly NntpSessionCoordinationOptions _options;
        private readonly ILogger<RedisSessionReconciliationCoordinator> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisSessionReconciliationCoordinator"/> class.
        /// </summary>
        /// <param name="redis">Redis connection accessor.</param>
        /// <param name="sessionDatabase">Node-local sessions used to identify live anchors.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="logger">Logger.</param>
        public RedisSessionReconciliationCoordinator(
            IRedisConnectionAccessor redis,
            ISessionDatabase sessionDatabase,
            IOptions<NntpSessionCoordinationOptions> options,
            ILogger<RedisSessionReconciliationCoordinator> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));
            ArgumentNullException.ThrowIfNull(options);
            _options = options.Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _keys = new RedisCoordinationKeys(_options.KeyPrefix);
        }

        /// <inheritdoc />
        public async Task<long> ReconcileAsync(string accountKey, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            cancellationToken.ThrowIfCancellationRequested();
            LogDebugRedisReconciliationStarted(accountKey);
            IDatabase db = _redis.GetDatabase();
            RedisKey liveSetKey = _keys.ReconciliationLiveSet(accountKey);
            try
            {
                await PurgeOrphanAnchorsAsync(db, accountKey, liveSetKey, cancellationToken).ConfigureAwait(false);

                RedisKey sessionsKey = _keys.Sessions(accountKey);
                RedisValue[] sessionArgv =
                [
                    _keys.SessionAnchorPattern(accountKey),
                    _options.ReconciliationMaxScanCalls,
                    _options.ReconciliationScanCount,
                    10_000,
                ];
                RedisResult sessionsResult = await db.ScriptEvaluateAsync(
                    RedisLuaScripts.SessionReconcileV1,
                    [sessionsKey],
                    sessionArgv).ConfigureAwait(false);
                long sessionsAfter = (long)sessionsResult;
                RedisKey ipsKey = _keys.Ips(accountKey);
                RedisValue[] ipArgv =
                [
                    _keys.IpSessionsPattern(accountKey),
                    _keys.IpSessionsPrefix(accountKey),
                    _options.ReconciliationMaxScanCalls,
                    _options.ReconciliationScanCount,
                    10_000,
                ];
                _ = await db.ScriptEvaluateAsync(RedisLuaScripts.IpReconcileV1, [ipsKey], ipArgv).ConfigureAwait(false);
                LogInformationRedisReconciliationCompleted(accountKey, sessionsAfter);
                return sessionsAfter;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarningRedisReconciliationFailed(accountKey, ex);
                throw;
            }
            finally
            {
                _ = await db.KeyDeleteAsync(liveSetKey).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Deletes Redis session anchors (and related IP sets) that are not tied to a live TCP session on this node.
        /// </summary>
        /// <param name="db">Redis database.</param>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="liveSetKey">Ephemeral set populated with live session ids.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when purge finishes.</returns>
        private async Task PurgeOrphanAnchorsAsync(
            IDatabase db,
            string accountKey,
            RedisKey liveSetKey,
            CancellationToken cancellationToken)
        {
            IReadOnlyCollection<string> liveSessionIds = _sessionDatabase.SnapshotSessionIdsForAccount(accountKey);
            await db.KeyDeleteAsync(liveSetKey).ConfigureAwait(false);
            if (liveSessionIds.Count > 0)
            {
                RedisValue[] members = liveSessionIds.Select(static id => (RedisValue)id).ToArray();
                _ = await db.SetAddAsync(liveSetKey, members).ConfigureAwait(false);
            }

            string anchorPrefix = _keys.SessionAnchorPrefix(accountKey);
            RedisKey[] redisKeys = [_keys.Sessions(accountKey), _keys.Ips(accountKey), liveSetKey];
            RedisValue[] argv =
            [
                _keys.SessionAnchorPattern(accountKey),
                anchorPrefix.Length,
                _keys.IpSessionsPattern(accountKey),
                _keys.IpSessionsPrefix(accountKey).Length,
                _options.ReconciliationMaxScanCalls,
                _options.ReconciliationScanCount,
                10_000,
            ];
            RedisResult purgeResult = await db.ScriptEvaluateAsync(
                RedisLuaScripts.AuthSessionPurgeOrphansV1,
                redisKeys,
                argv).ConfigureAwait(false);
            long purged = (long)purgeResult;
            if (purged > 0)
            {
                LogInformationRedisOrphanAnchorsPurged(accountKey, purged, liveSessionIds.Count);
            }
        }
    }
}
