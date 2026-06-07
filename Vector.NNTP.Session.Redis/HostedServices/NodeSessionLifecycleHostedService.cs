// <copyright file="NodeSessionLifecycleHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>
    /// Purges orphaned Redis session registry entries on host startup and after graceful shutdown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register before heartbeat and socket listeners so <see cref="StartAsync"/> runs first on startup.
    /// On shutdown, hosted services stop in reverse registration order; socket accept and in-flight drain
    /// complete before this service releases survivors and purges the node index.
    /// </para>
    /// <para>
    /// <see cref="NodeSessionRegistryFields.LeaseUpdated"/> is informational; Redis key TTL is authoritative for liveness.
    /// </para>
    /// </remarks>
    internal sealed partial class NodeSessionLifecycleHostedService : IHostedService
    {
        /// <summary>
        /// Redis-backed registry used to purge orphaned leases for this node.
        /// </summary>
        private readonly INodeSessionRegistry _nodeRegistry;

        /// <summary>
        /// Node-local session database scanned during shutdown survivor release.
        /// </summary>
        private readonly ISessionDatabase _sessionDatabase;

        /// <summary>
        /// Auth admission coordinator for releasing survivor authenticated sessions.
        /// </summary>
        private readonly INntpSessionCoordinator _sessionCoordinator;

        /// <summary>
        /// Transit peer coordinator for releasing survivor transit sessions.
        /// </summary>
        private readonly INntpTransitPeerCoordinator _transitPeerCoordinator;

        /// <summary>
        /// Bound node identity supplying <see cref="NntpNodeIdentityOptions.NodeName"/>.
        /// </summary>
        private readonly IOptions<NntpNodeIdentityOptions> _nodeOptions;

        /// <summary>
        /// Logger for startup/shutdown purge completion and survivor release failures.
        /// </summary>
        private readonly ILogger<NodeSessionLifecycleHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeSessionLifecycleHostedService"/> class.
        /// </summary>
        /// <param name="nodeRegistry">Node session registry.</param>
        /// <param name="sessionDatabase">Node-local session database.</param>
        /// <param name="sessionCoordinator">Auth admission coordinator.</param>
        /// <param name="transitPeerCoordinator">Transit peer coordinator.</param>
        /// <param name="nodeOptions">Node identity options.</param>
        /// <param name="logger">Logger.</param>
        public NodeSessionLifecycleHostedService(
            INodeSessionRegistry nodeRegistry,
            ISessionDatabase sessionDatabase,
            INntpSessionCoordinator sessionCoordinator,
            INntpTransitPeerCoordinator transitPeerCoordinator,
            IOptions<NntpNodeIdentityOptions> nodeOptions,
            ILogger<NodeSessionLifecycleHostedService> logger)
        {
            _nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
            _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));
            _sessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
            _transitPeerCoordinator = transitPeerCoordinator ?? throw new ArgumentNullException(nameof(transitPeerCoordinator));
            _nodeOptions = nodeOptions ?? throw new ArgumentNullException(nameof(nodeOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Purges orphaned Redis leases indexed for this node before other coordination hosted services start.
        /// </summary>
        /// <param name="cancellationToken">Host startup cancellation token.</param>
        /// <returns>A task that completes when the startup purge finishes.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            string nodeName = _nodeOptions.Value.NodeName;
            NodeSessionPurgeResult result = await _nodeRegistry.PurgeNodeAsync(nodeName, cancellationToken).ConfigureAwait(false);
            LogInformationStartupPurgeCompleted(
                _logger,
                nodeName,
                result.AuthLeasesPurged,
                result.TransitLeasesPurged,
                result.DurationMs);
        }

        /// <summary>
        /// Releases distributed leases for node-local survivors, then purges the node index after connection drain.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown cancellation token.</param>
        /// <returns>A task that completes when survivor release and shutdown purge finish.</returns>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            string nodeName = _nodeOptions.Value.NodeName;
            await ReleaseSurvivorsAsync(nodeName, cancellationToken).ConfigureAwait(false);
            NodeSessionPurgeResult result = await _nodeRegistry.PurgeNodeAsync(nodeName, cancellationToken).ConfigureAwait(false);
            LogInformationShutdownPurgeCompleted(
                _logger,
                nodeName,
                result.AuthLeasesPurged,
                result.TransitLeasesPurged,
                result.DurationMs);
        }

        /// <summary>
        /// Releases distributed leases for sessions still present in the node-local database after connection drain.
        /// </summary>
        /// <param name="nodeName">Stable node identity.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when release attempts finish.</returns>
        private async Task ReleaseSurvivorsAsync(string nodeName, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<SessionContext> survivors = _sessionDatabase.SnapshotAll();
            foreach (SessionContext session in survivors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(session.TransitPeerName))
                {
                    try
                    {
                        await _transitPeerCoordinator
                            .ReleaseAsync(session.TransitPeerName, session.SessionId, nodeName, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogWarningSurvivorTransitReleaseFailed(_logger, ex, session.SessionId, session.TransitPeerName);
                    }

                    continue;
                }

                if (session.SessionPolicy is not NntpSessionPolicy policy || !policy.RequiresDistributedAdmission())
                {
                    continue;
                }

                try
                {
                    await _sessionCoordinator
                        .ReleaseAsync(
                            policy,
                            session.SessionId,
                            session.RemoteIp.ToString(),
                            session.NodeName,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogWarningSurvivorAuthReleaseFailed(_logger, ex, session.SessionId, policy.AccountKey);
                }
            }
        }
    }
}
