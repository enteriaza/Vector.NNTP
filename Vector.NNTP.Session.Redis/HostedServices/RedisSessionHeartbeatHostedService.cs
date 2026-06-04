// <copyright file="RedisSessionHeartbeatHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using Vector.NNTP.Session.Coordination;
using Vector.NNTP.Session.Utilities;

namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>
    /// Periodically refreshes Redis leases for authenticated sessions on this node.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="RedisSessionHeartbeatHostedService"/> class.
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
        /// Session database.
        /// </summary>
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));

        /// <summary>
        /// Lease refresher.
        /// </summary>
        private readonly IRedisSessionLeaseRefresher _leaseRefresher = leaseRefresher ?? throw new ArgumentNullException(nameof(leaseRefresher));

        /// <summary>
        /// Transit peer coordinator.
        /// </summary>
        private readonly INntpTransitPeerCoordinator _transitPeerCoordinator =
            transitPeerCoordinator ?? throw new ArgumentNullException(nameof(transitPeerCoordinator));

        /// <summary>
        /// Coordination options.
        /// </summary>
        private readonly IOptionsMonitor<NntpSessionCoordinationOptions> _coordinationOptions = coordinationOptions ?? throw new ArgumentNullException(nameof(coordinationOptions));

        /// <summary>
        /// Idle options.
        /// </summary>
        private readonly IOptionsMonitor<NntpSessionIdleOptions> _idleOptions = idleOptions ?? throw new ArgumentNullException(nameof(idleOptions));

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<RedisSessionHeartbeatHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Execute the heartbeat hosted service.
        /// </summary>
        /// <param name="stoppingToken">Token to stop the hosted service.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
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
                int ttlSeconds = NntpSessionTtlCalculator.ComputeTtlSeconds(_idleOptions.CurrentValue.IdleTimeout);
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
                    if (string.IsNullOrEmpty(transit.TransitPeerId))
                    {
                        continue;
                    }

                    try
                    {
                        await _transitPeerCoordinator.RefreshLeaseAsync(
                            transit.TransitPeerId,
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
                        LogWarningTransitPeerHeartbeatFailed(this._logger, ex, transit.SessionId, transit.TransitPeerId);
                    }
                }
            }
        }
    }
}
