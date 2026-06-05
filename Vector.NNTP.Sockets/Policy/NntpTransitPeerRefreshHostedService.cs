// <copyright file="NntpTransitPeerRefreshHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: periodic DNS refresh for transit peer matcher snapshots.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.Metrics;

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>
    /// Periodically rebuilds the <see cref="NntpTransitPeerMatcher"/> DNS snapshot and reconciles Redis ZSET counts.
    /// </summary>
    public sealed partial class NntpTransitPeerRefreshHostedService : BackgroundService
    {
        private readonly NntpTransitPeerMatcher _matcher;
        private readonly IOptionsMonitor<NntpServerOptions> _options;
        private readonly INntpTransitPeerCoordinator? _coordinator;
        private readonly ILogger<NntpTransitPeerRefreshHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpTransitPeerRefreshHostedService"/> class.
        /// </summary>
        /// <param name="matcher">Transit peer matcher.</param>
        /// <param name="options">Server options.</param>
        /// <param name="coordinator">Transit peer coordinator for capacity reconciliation.</param>
        /// <param name="logger">Logger.</param>
        public NntpTransitPeerRefreshHostedService(
            NntpTransitPeerMatcher matcher,
            IOptionsMonitor<NntpServerOptions> options,
            INntpTransitPeerCoordinator coordinator,
            ILogger<NntpTransitPeerRefreshHostedService> logger)
        {
            _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                NntpTransitPeersOptions transitPeers = _options.CurrentValue.TransitPeers;
                TimeSpan interval = transitPeers.Peers is null || transitPeers.Peers.Length == 0
                    ? TimeSpan.FromMinutes(1)
                    : TimeSpan.FromMinutes(Math.Max(1, transitPeers.RefreshIntervalMinutes));
                if (transitPeers.Peers is not null && transitPeers.Peers.Length > 0)
                {
                    await RefreshAndReconcileAsync(transitPeers, stoppingToken).ConfigureAwait(false);
                }

                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        /// <summary>Rebuilds the DNS snapshot and purges stale Redis ZSET members for each peer.</summary>
        /// <param name="transitPeers">Configured transit peers.</param>
        /// <param name="stoppingToken">Cancellation token.</param>
        /// <returns>A task that completes when refresh and reconciliation finish.</returns>
        private async Task RefreshAndReconcileAsync(NntpTransitPeersOptions transitPeers, CancellationToken stoppingToken)
        {
            if (!_matcher.TryRebuildSnapshot(logSuccess: true, out string? error))
            {
                LogRefreshRetainedPrevious(_logger, error ?? "unknown");
            }

            if (_coordinator is null || transitPeers.Peers is null)
            {
                return;
            }

            foreach (NntpTransitPeerOptions peer in transitPeers.Peers)
            {
                try
                {
                    long count = await _coordinator
                        .ReconcileCapacityAsync(peer.PeerId, stoppingToken)
                        .ConfigureAwait(false);
                    NntpTransitPeerMetrics.UpdateCurrentCapacity(peer.PeerId, count);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogCapacityReconcileFailed(_logger, ex, peer.PeerId);
                }
            }
        }
    }
}
