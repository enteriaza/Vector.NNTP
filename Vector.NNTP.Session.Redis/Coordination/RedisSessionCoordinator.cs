// <copyright file="RedisSessionCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using System.Diagnostics;
using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Coordination
{
    /// <summary>
    /// Redis Lua-backed <see cref="INntpSessionCoordinator"/> for cluster-wide admission.
    /// </summary>
    public sealed partial class RedisSessionCoordinator : INntpSessionCoordinator
    {
        private readonly IRedisConnectionAccessor _redis;
        private readonly RedisCoordinationKeys _keys;
        private readonly IRedisSessionReconciliationCoordinator _reconciliation;
        private readonly ILogger<RedisSessionCoordinator> _logger;
        private readonly int _slowThresholdMs;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisSessionCoordinator"/> class.
        /// </summary>
        /// <param name="redis">Redis connection accessor.</param>
        /// <param name="reconciliation">Reconciliation coordinator.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="logger">Logger.</param>
        public RedisSessionCoordinator(
            IRedisConnectionAccessor redis,
            IRedisSessionReconciliationCoordinator reconciliation,
            IOptions<NntpSessionCoordinationOptions> options,
            ILogger<RedisSessionCoordinator> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
            ArgumentNullException.ThrowIfNull(options);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _keys = new RedisCoordinationKeys(options.Value.KeyPrefix);
            _slowThresholdMs = Math.Max(0, options.Value.SlowRedisCallThresholdMs);
        }

        /// <inheritdoc />
        public async ValueTask<NntpSessionAdmissionResult> TryAdmitAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(clientIpText);
            cancellationToken.ThrowIfCancellationRequested();
            if (!policy.RequiresDistributedAdmission())
            {
                return NntpSessionAdmissionResult.Success;
            }

            if (policy.SessionLimit <= 0 && policy.SrcIpLimit <= 0)
            {
                return NntpSessionAdmissionResult.PolicyInvalid;
            }

            int maxSessions = policy.SessionLimit > 0 ? policy.SessionLimit : int.MaxValue;
            int ipLimit = policy.SrcIpLimit > 0 ? policy.SrcIpLimit : int.MaxValue;
            string accountKey = policy.AccountKey;
            try
            {
                long code = await EvaluateAcquireAsync(accountKey, sessionId, clientIpText, maxSessions, ipLimit, ttlSeconds, cancellationToken)
                    .ConfigureAwait(false);
                if (code is 1 or 2)
                {
                    try
                    {
                        _ = await _reconciliation.ReconcileAsync(accountKey, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogWarningRedisReconciliationFailed(accountKey, ex);
                    }

                    code = await EvaluateAcquireAsync(accountKey, sessionId, clientIpText, maxSessions, ipLimit, ttlSeconds, cancellationToken)
                        .ConfigureAwait(false);
                }

                return MapAcquireResult(code, policy.Username, clientIpText);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogWarningSessionAdmissionBackendFailure(policy.Username, ex);
                return NntpSessionAdmissionResult.BackendFailure;
            }
        }

        /// <inheritdoc />
        public async ValueTask ReleaseAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(clientIpText);
            cancellationToken.ThrowIfCancellationRequested();
            if (!policy.RequiresDistributedAdmission())
            {
                return;
            }

            string accountKey = policy.AccountKey;
            RedisKey[] redisKeys =
            [
                _keys.Sessions(accountKey),
                _keys.Ips(accountKey),
                _keys.IpSessions(accountKey, clientIpText),
                _keys.SessionAnchor(accountKey, sessionId),
            ];
            RedisValue[] argv = [sessionId, clientIpText];
            IDatabase db = _redis.GetDatabase();
            _ = await db.ScriptEvaluateAsync(RedisLuaScripts.AuthSessionReleaseV1, redisKeys, argv).ConfigureAwait(false);
        }

        /// <summary>
        /// Evaluates the acquire Lua script against Redis.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="ipText">Client IP text.</param>
        /// <param name="maxSessions">Maximum concurrent sessions.</param>
        /// <param name="ipLimit">Maximum distinct source IPs.</param>
        /// <param name="ttlSeconds">Lease TTL seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Lua script return code.</returns>
        private async Task<long> EvaluateAcquireAsync(
            string accountKey,
            string sessionId,
            string ipText,
            int maxSessions,
            int ipLimit,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RedisKey[] redisKeys =
            [
                _keys.Sessions(accountKey),
                _keys.Ips(accountKey),
                _keys.IpSessions(accountKey, ipText),
                _keys.SessionAnchor(accountKey, sessionId),
            ];
            RedisValue[] argv = [sessionId, ipText, maxSessions, ipLimit, ttlSeconds];
            IDatabase db = _redis.GetDatabase();
            Stopwatch sw = Stopwatch.StartNew();
            RedisResult result = await db.ScriptEvaluateAsync(RedisLuaScripts.AuthSessionAcquireV1, redisKeys, argv).ConfigureAwait(false);
            double elapsedMs = sw.Elapsed.TotalMilliseconds;
            if (_slowThresholdMs > 0 && elapsedMs >= _slowThresholdMs)
            {
                LogWarningRedisOperationSlow("account-sharing-acquire", elapsedMs);
                _redis.SignalScaleUp();
            }

            return (long)result;
        }

        /// <summary>
        /// Maps Lua acquire return codes to admission results and logs outcomes.
        /// </summary>
        /// <param name="code">Lua return code.</param>
        /// <param name="username">Authenticated username.</param>
        /// <param name="clientIp">Client IP text.</param>
        /// <returns>Mapped admission result.</returns>
        private NntpSessionAdmissionResult MapAcquireResult(long code, string username, string clientIp)
        {
            NntpSessionAdmissionResult outcome = code switch
            {
                0 => NntpSessionAdmissionResult.Success,
                1 => NntpSessionAdmissionResult.MaxSessionsExceeded,
                2 => NntpSessionAdmissionResult.IpLimitExceeded,
                _ => NntpSessionAdmissionResult.MaxSessionsExceeded,
            };
            if (outcome == NntpSessionAdmissionResult.Success)
            {
                LogInformationSessionAdmissionGranted(username, clientIp);
            }
            else
            {
                string reason = outcome == NntpSessionAdmissionResult.IpLimitExceeded ? "IpLimit" : "MaxSessions";
                LogInformationSessionAdmissionDenied(username, clientIp, reason);
            }

            return outcome;
        }
    }
}
