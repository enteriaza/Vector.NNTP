// <copyright file="RedisSessionHeartbeatHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>
    /// Periodically refreshes Redis leases for authenticated and transit peer sessions on this node.
    /// </summary>
    /// <remarks>
    /// Runs on <see cref="NntpSessionCoordinationOptions.HeartbeatIntervalSeconds"/> and extends TTLs using
    /// idle-timeout-derived lease sizing. Individual heartbeat failures are logged and do not stop the loop.
    /// </remarks>
    /// <param name="sessionDatabase">Node-local sessions.</param>
    /// <param name="leaseRefresher">Lease refresher.</param>
    /// <param name="transitPeerCoordinator">Transit peer ZSET lease coordinator.</param>
    /// <param name="coordinationOptions">Redis coordination options.</param>
    /// <param name="idleOptions">Resolved idle timeout for TTL sizing.</param>
    /// <param name="logger">Logger.</param>
    public sealed partial class RedisSessionHeartbeatHostedService(
        ISessionDatabase sessionDatabase,
        IRedisSessionLeaseRefresher leaseRefresher,
        INntpTransitPeerCoordinator transitPeerCoordinator,
        IOptionsMonitor<NntpSessionCoordinationOptions> coordinationOptions,
        IOptionsMonitor<NntpSessionIdleOptions> idleOptions,
        ILogger<RedisSessionHeartbeatHostedService> logger) : BackgroundService
    {
        /// <summary>
        /// Node-local session database supplying authenticated and transit peer snapshots each sweep.
        /// </summary>
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));

        /// <summary>
        /// Redis lease refresher invoked for authenticated session anchors.
        /// </summary>
        private readonly IRedisSessionLeaseRefresher _leaseRefresher = leaseRefresher ?? throw new ArgumentNullException(nameof(leaseRefresher));

        /// <summary>
        /// Transit peer coordinator invoked to refresh ZSET scores for transit sessions.
        /// </summary>
        private readonly INntpTransitPeerCoordinator _transitPeerCoordinator =
            transitPeerCoordinator ?? throw new ArgumentNullException(nameof(transitPeerCoordinator));

        /// <summary>
        /// Monitored coordination options supplying heartbeat interval and TTL floors.
        /// </summary>
        private readonly IOptionsMonitor<NntpSessionCoordinationOptions> _coordinationOptions = coordinationOptions ?? throw new ArgumentNullException(nameof(coordinationOptions));

        /// <summary>
        /// Monitored idle timeout options used to size lease TTL seconds.
        /// </summary>
        private readonly IOptionsMonitor<NntpSessionIdleOptions> _idleOptions = idleOptions ?? throw new ArgumentNullException(nameof(idleOptions));

        /// <summary>
        /// Logger for per-session heartbeat failures.
        /// </summary>
        private readonly ILogger<RedisSessionHeartbeatHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Delays on the configured heartbeat interval and refreshes leases for all live sessions until shutdown.
        /// </summary>
        /// <param name="stoppingToken">Token signaled when the host is stopping.</param>
        /// <returns>A task that runs until <paramref name="stoppingToken"/> is canceled.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                NntpSessionCoordinationOptions coordination = _coordinationOptions.CurrentValue;
                TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, coordination.HeartbeatIntervalSeconds));
                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                NntpSessionCoordinationOptions coordinationSnapshot = _coordinationOptions.CurrentValue;
                int ttlSeconds = NntpSessionTtlCalculator.ComputeTtlSeconds(_idleOptions.CurrentValue.IdleTimeoutSeconds);
                int transitLeaseSeconds = NntpSessionTtlCalculator.ComputeTransitPeerLeaseSeconds(
                    coordinationSnapshot.HeartbeatIntervalSeconds,
                    coordinationSnapshot.TtlMinimumSeconds);
                IReadOnlyCollection<SessionContext> sessions = _sessionDatabase.SnapshotAuthenticated();
                foreach (SessionContext session in sessions)
                {
                    if (string.IsNullOrEmpty(session.AccountKey))
                    {
                        continue;
                    }

                    try
                    {
                        await _leaseRefresher.HeartbeatAsync(
                            session.AccountKey,
                            session.SessionId,
                            session.RemoteIp.ToString(),
                            session.NodeName,
                            ttlSeconds,
                            stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogWarningHeartbeatFailed(this._logger, ex, session.SessionId, session.AccountKey);
                    }
                }

                IReadOnlyCollection<SessionContext> transitPeers = _sessionDatabase.SnapshotTransitPeers();
                foreach (SessionContext transit in transitPeers)
                {
                    if (string.IsNullOrEmpty(transit.TransitPeerName))
                    {
                        continue;
                    }

                    try
                    {
                        await _transitPeerCoordinator.RefreshLeaseAsync(
                            transit.TransitPeerName,
                            transit.SessionId,
                            transit.NodeName,
                            transitLeaseSeconds,
                            stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogWarningTransitPeerHeartbeatFailed(this._logger, ex, transit.SessionId, transit.TransitPeerName);
                    }
                }
            }
        }
    }
}
