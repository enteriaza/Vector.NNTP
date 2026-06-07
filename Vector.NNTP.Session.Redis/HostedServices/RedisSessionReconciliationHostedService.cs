// <copyright file="RedisSessionReconciliationHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>
    /// Periodic bounded reconciliation sweep for distinct accounts on this node.
    /// </summary>
    /// <remarks>
    /// Registered only when <see cref="NntpSessionCoordinationOptions.ReconciliationIntervalSeconds"/> is positive.
    /// Each sweep reconciles every distinct account key present in the node-local session database.
    /// </remarks>
    /// <param name="reconciliationCoordinator">Reconciliation coordinator.</param>
    /// <param name="sessionDatabase">Session database for account keys.</param>
    /// <param name="coordinationOptions">Coordination options.</param>
    /// <param name="logger">Logger.</param>
    public sealed partial class RedisSessionReconciliationHostedService(
        IRedisSessionReconciliationCoordinator reconciliationCoordinator,
        ISessionDatabase sessionDatabase,
        IOptionsMonitor<NntpSessionCoordinationOptions> coordinationOptions,
        ILogger<RedisSessionReconciliationHostedService> logger) : BackgroundService
    {
        /// <summary>
        /// Bounded reconciliation coordinator invoked once per distinct account key.
        /// </summary>
        private readonly IRedisSessionReconciliationCoordinator _reconciliationCoordinator = reconciliationCoordinator ?? throw new ArgumentNullException(nameof(reconciliationCoordinator));

        /// <summary>
        /// Node-local session database supplying distinct account keys for each sweep.
        /// </summary>
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));

        /// <summary>
        /// Monitored coordination options supplying the reconciliation interval.
        /// </summary>
        private readonly IOptionsMonitor<NntpSessionCoordinationOptions> _coordinationOptions = coordinationOptions ?? throw new ArgumentNullException(nameof(coordinationOptions));

        /// <summary>
        /// Logger for per-account reconciliation failures.
        /// </summary>
        private readonly ILogger<RedisSessionReconciliationHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Waits on the reconciliation interval and runs bounded orphan cleanup per account until shutdown.
        /// </summary>
        /// <param name="stoppingToken">Token signaled when the host is stopping.</param>
        /// <returns>A task that runs until <paramref name="stoppingToken"/> is canceled.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                NntpSessionCoordinationOptions opts = _coordinationOptions.CurrentValue;
                int interval = opts.ReconciliationIntervalSeconds;
                if (interval <= 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken).ConfigureAwait(false);
                foreach (string accountKey in _sessionDatabase.SnapshotDistinctAccountKeys())
                {
                    try
                    {
                        _ = await _reconciliationCoordinator.ReconcileAsync(accountKey, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        RedisSessionReconciliationHostedServiceLog.RedisReconciliationFailed(_logger, ex, accountKey);
                    }
                }
            }
        }
    }
}
