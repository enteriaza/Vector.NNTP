// <copyright file="RedisSessionReconciliationHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.HostedServices
{
    /// <summary>
    /// Periodic bounded reconciliation sweep for distinct accounts on this node.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="RedisSessionReconciliationHostedService"/> class.
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
        private readonly IRedisSessionReconciliationCoordinator _reconciliationCoordinator = reconciliationCoordinator ?? throw new ArgumentNullException(nameof(reconciliationCoordinator));
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));
        private readonly IOptionsMonitor<NntpSessionCoordinationOptions> _coordinationOptions = coordinationOptions ?? throw new ArgumentNullException(nameof(coordinationOptions));
        private readonly ILogger<RedisSessionReconciliationHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
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
