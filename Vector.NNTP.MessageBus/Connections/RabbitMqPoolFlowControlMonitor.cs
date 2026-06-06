// <copyright file="RabbitMqPoolFlowControlMonitor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPoolFlowControlMonitor.cs -- Hosted service quarantining broker-blocked pooled connections.
//
// Periodically calls ConnectionPool.EnforceBlockedQuarantine and refreshes IRabbitMqPoolHealth. Scan interval is derived
// from ConnectionBlockedTimeout with clamped bounds.
//
// Thread safety:
//   Single hosted-service loop; pool APIs are thread-safe.
//
// Logging: [LoggerMessage] partial methods in RabbitMqPoolFlowControlMonitor.Logging.cs.

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Health;

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Hosted service that quarantines pooled connections remaining broker-blocked past
    /// <see cref="RabbitMQOptions.ConnectionBlockedTimeout"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Policy:</b> Blocked connections are not faulted immediately. Prolonged blocking sets
    /// <see cref="PooledConnection.IsStalled"/> so new publisher slots route to healthier TCP connections while the broker
    /// recovers.</para>
    /// <para><b>Scan interval:</b> One tenth of <see cref="RabbitMQOptions.ConnectionBlockedTimeout"/>, clamped between
    /// <see cref="MinimumScanInterval"/> and <see cref="MaximumScanInterval"/>.</para>
    /// <para><b>Health:</b> Refreshes <see cref="IRabbitMqPoolHealth"/> after each scan.</para>
    /// <para><b>Failure handling:</b> Scan exceptions are logged; the loop continues after
    /// <see cref="MinimumScanInterval"/>.</para>
    /// </remarks>
    /// <param name="pool">Connection pool to monitor.</param>
    /// <param name="health">Health surface to refresh after each scan.</param>
    /// <param name="options">RabbitMQ options (<see cref="RabbitMQOptions.ConnectionBlockedTimeout"/>).</param>
    /// <param name="logger">Logger for source-generated <c>[LoggerMessage]</c> methods.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pool"/>, <paramref name="health"/>, or
    /// <paramref name="options"/> is <see langword="null"/>.</exception>
    internal sealed partial class RabbitMqPoolFlowControlMonitor(
        ConnectionPool pool,
        IRabbitMqPoolHealth health,
        IOptions<RabbitMQOptions> options,
        ILogger<RabbitMqPoolFlowControlMonitor> logger) : BackgroundService
    {
        /// <summary>Lower bound for periodic scan delay.</summary>
        private static readonly TimeSpan MinimumScanInterval = TimeSpan.FromSeconds(1);

        /// <summary>Upper bound for periodic scan delay.</summary>
        private static readonly TimeSpan MaximumScanInterval = TimeSpan.FromSeconds(30);

        /// <summary>Connection pool to inspect.</summary>
        private readonly ConnectionPool _pool = pool ?? throw new ArgumentNullException(nameof(pool));

        /// <summary>Pool health aggregator.</summary>
        private readonly IRabbitMqPoolHealth _health = health ?? throw new ArgumentNullException(nameof(health));

        /// <summary>RabbitMQ configuration snapshot.</summary>
        private readonly IOptions<RabbitMQOptions> _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>
        /// Periodically enforces stalled quarantine and updates pool health until shutdown.
        /// </summary>
        /// <param name="stoppingToken">Host shutdown token.</param>
        /// <returns>A task representing the background scan loop.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    RabbitMQOptions options = _options.Value;
                    TimeSpan scanInterval = ResolveScanInterval(options.ConnectionBlockedTimeout);
                    int stalled = _pool.EnforceBlockedQuarantine(options.ConnectionBlockedTimeout);
                    if (stalled > 0)
                        LogConnectionsStalled(stalled, options.ConnectionBlockedTimeout);
                    _health.UpdateFromPool(_pool, options);
                    await Task.Delay(scanInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogFlowControlScanError(ex);
                    await Task.Delay(MinimumScanInterval, stoppingToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Derives the delay between flow-control scans from the blocked-timeout setting.</summary>
        /// <param name="blockedTimeout">Configured <see cref="RabbitMQOptions.ConnectionBlockedTimeout"/>.</param>
        /// <returns>Clamped scan interval.</returns>
        private static TimeSpan ResolveScanInterval(TimeSpan blockedTimeout)
        {
            if (blockedTimeout <= TimeSpan.Zero)
                return MinimumScanInterval;
            double milliseconds = blockedTimeout.TotalMilliseconds / 10d;
            TimeSpan interval = TimeSpan.FromMilliseconds(milliseconds);
            return interval < MinimumScanInterval
                ? MinimumScanInterval
                : interval > MaximumScanInterval ? MaximumScanInterval : interval;
        }
    }
}
