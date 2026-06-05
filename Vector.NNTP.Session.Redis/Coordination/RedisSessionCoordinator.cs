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
        /// <summary>
        /// Redis connection accessor.
        /// </summary>
        private readonly IRedisConnectionAccessor _redis;

        /// <summary>
        /// Redis coordination keys.
        /// </summary>
        private readonly RedisCoordinationKeys _keys;

        /// <summary>
        /// Reconciliation coordinator.
        /// </summary>
        private readonly IRedisSessionReconciliationCoordinator _reconciliation;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<RedisSessionCoordinator> _logger;

        /// <summary>
        /// The threshold in milliseconds for a slow Redis operation.
        /// </summary>
        /// <remarks>
        /// If the elapsed time is greater than or equal to this threshold, a warning will be logged.
        /// </remarks>
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

        /// <summary>
        /// Tries to admit a session.
        /// </summary>
        /// <param name="policy">The session policy.</param>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="clientIpText">The client IP text.</param>
        /// <param name="nodeName">Stable cluster node identity.</param>
        /// <param name="ttlSeconds">The TTL seconds.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The admission result.</returns>
        public async ValueTask<NntpSessionAdmissionResult> TryAdmitAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            string nodeName,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(clientIpText);
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
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
                long code = await EvaluateAcquireAsync(
                        accountKey,
                        sessionId,
                        clientIpText,
                        nodeName,
                        maxSessions,
                        ipLimit,
                        ttlSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (code is 1 or 2)
                {
                    try
                    {
                        _ = await _reconciliation.ReconcileAsync(accountKey, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogWarningRedisReconciliationFailed(_logger, ex, accountKey);
                    }

                    code = await EvaluateAcquireAsync(
                            accountKey,
                            sessionId,
                            clientIpText,
                            nodeName,
                            maxSessions,
                            ipLimit,
                            ttlSeconds,
                            cancellationToken)
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
                LogWarningSessionAdmissionBackendFailure(_logger, ex, policy.Username);
                return NntpSessionAdmissionResult.BackendFailure;
            }
        }

        /// <summary>
        /// Releases a session.
        /// </summary>
        /// <param name="policy">The session policy.</param>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="clientIpText">The client IP text.</param>
        /// <param name="nodeName">Stable cluster node identity.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The admission result.</returns>
        public async ValueTask ReleaseAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            string nodeName,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(clientIpText);
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
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
                _keys.SessionMeta(sessionId),
                _keys.NodeSessions(nodeName),
            ];
            RedisValue[] argv = [sessionId, clientIpText];
            IDatabase db = _redis.GetDatabase();
            _ = await db.ScriptEvaluateAsync(RedisLuaScripts.AuthSessionReleaseV2, redisKeys, argv).ConfigureAwait(false);
        }

        /// <summary>
        /// Evaluates the acquire Lua script against Redis.
        /// </summary>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="ipText">Client IP text.</param>
        /// <param name="nodeName">Stable cluster node identity.</param>
        /// <param name="maxSessions">Maximum concurrent sessions.</param>
        /// <param name="ipLimit">Maximum distinct source IPs.</param>
        /// <param name="ttlSeconds">Lease TTL seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Lua script return code.</returns>
        private async Task<long> EvaluateAcquireAsync(
            string accountKey,
            string sessionId,
            string ipText,
            string nodeName,
            int maxSessions,
            int ipLimit,
            int ttlSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int metaTtl = NntpSessionTtlCalculator.ComputeMetadataTtlSeconds(ttlSeconds);
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            RedisKey[] redisKeys =
            [
                _keys.Sessions(accountKey),
                _keys.Ips(accountKey),
                _keys.IpSessions(accountKey, ipText),
                _keys.SessionAnchor(accountKey, sessionId),
                _keys.SessionMeta(sessionId),
                _keys.NodeSessions(nodeName),
            ];
            RedisValue[] argv =
            [
                sessionId,
                ipText,
                maxSessions,
                ipLimit,
                ttlSeconds,
                RedisValue.EmptyString,
                nodeName,
                accountKey,
                nowMs,
                metaTtl,
            ];
            IDatabase db = _redis.GetDatabase();
            Stopwatch sw = Stopwatch.StartNew();
            RedisResult result = await db.ScriptEvaluateAsync(RedisLuaScripts.AuthSessionAcquireV2, redisKeys, argv).ConfigureAwait(false);
            double elapsedMs = sw.Elapsed.TotalMilliseconds;
            if (_slowThresholdMs > 0 && elapsedMs >= _slowThresholdMs)
            {
                LogWarningRedisOperationSlow(_logger, "account-sharing-acquire", elapsedMs);
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
                LogInformationSessionAdmissionGranted(_logger, username, clientIp);
            }
            else
            {
                string reason = outcome == NntpSessionAdmissionResult.IpLimitExceeded ? "IpLimit" : "MaxSessions";
                LogInformationSessionAdmissionDenied(_logger, username, clientIp, reason);
            }

            return outcome;
        }
    }
}
