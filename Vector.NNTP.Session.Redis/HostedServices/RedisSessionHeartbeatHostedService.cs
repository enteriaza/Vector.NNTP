// <copyright file="RedisSessionHeartbeatHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
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
    /// <param name="coordinationOptions">Redis coordination options.</param>
    /// <param name="idleOptions">Resolved idle timeout for TTL sizing.</param>
    /// <param name="logger">Logger.</param>
    public sealed partial class RedisSessionHeartbeatHostedService(
        ISessionDatabase sessionDatabase,
        IRedisSessionLeaseRefresher leaseRefresher,
        IOptionsMonitor<NntpSessionCoordinationOptions> coordinationOptions,
        IOptionsMonitor<NntpSessionIdleOptions> idleOptions,
        ILogger<RedisSessionHeartbeatHostedService> logger) : BackgroundService
    {
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));
        private readonly IRedisSessionLeaseRefresher _leaseRefresher = leaseRefresher ?? throw new ArgumentNullException(nameof(leaseRefresher));
        private readonly IOptionsMonitor<NntpSessionCoordinationOptions> _coordinationOptions = coordinationOptions ?? throw new ArgumentNullException(nameof(coordinationOptions));
        private readonly IOptionsMonitor<NntpSessionIdleOptions> _idleOptions = idleOptions ?? throw new ArgumentNullException(nameof(idleOptions));
        private readonly ILogger<RedisSessionHeartbeatHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
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

                int ttlSeconds = NntpSessionTtlCalculator.ComputeTtlSeconds(_idleOptions.CurrentValue.IdleTimeout);
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
                            ttlSeconds,
                            stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogWarningHeartbeatFailed(session.SessionId, session.AccountKey, ex);
                    }
                }
            }
        }
    }
}
