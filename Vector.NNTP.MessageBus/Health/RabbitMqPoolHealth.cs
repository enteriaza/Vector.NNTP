// <copyright file="RabbitMqPoolHealth.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RabbitMqPoolHealth.cs -- Aggregate RabbitMQ connection-pool health derived from live TCP snapshots.
//
// Computes Healthy / Degraded / Unhealthy from the fraction of connections in PooledConnectionState.Connected,
// using thresholds from RabbitMQOptions. Refreshed by RabbitMqPoolSupervisor at startup and by
// RabbitMqPoolFlowControlMonitor after each flow-control scan.
//
// Thread safety:
//   Status is updated only from hosted-service call paths; no locking. Hosts should treat reads as eventually
//   consistent with the last UpdateFromPool call.
//
// Logging:
//   Emits OpenTelemetry counters via MessageBusMeters.RecordHealth; no ILogger on this type.

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Metrics;

namespace Vector.NNTP.MessageBus.Health
{
    /// <summary>
    /// Tracks aggregate pool health from <see cref="ConnectionPool"/> connection snapshots.
    /// </summary>
    /// <remarks>
    /// <para><b>Policy:</b> Health is derived from the ratio of connections in
    /// <see cref="PooledConnectionState.Connected"/> to total pooled connections. Threshold comparisons use
    /// <see cref="RabbitMQOptions.DegradedThreshold"/> and <see cref="RabbitMQOptions.UnhealthyThreshold"/> on the
    /// faulted fraction (<c>1 - connected/total</c>).</para>
    ///
    /// <para><b>Empty pool:</b> When <see cref="ConnectionPool.Snapshot"/> is empty, status is
    /// <see cref="PoolHealthStatus.Unhealthy"/> — the pool cannot serve publisher or consumer traffic.</para>
    ///
    /// <para><b>Initial state:</b> <see cref="Status"/> defaults to <see cref="PoolHealthStatus.Recovering"/> until the
    /// first <see cref="UpdateFromPool"/> after <see cref="RabbitMqPoolSupervisor.StartAsync"/>.</para>
    ///
    /// <para><b>Observability:</b> Each transition records a bounded OpenTelemetry counter via
    /// <see cref="MessageBusMeters.RecordHealth(string)"/> with a lowercase status label.</para>
    ///
    /// <para><b>Thread safety:</b> No synchronisation; callers should invoke <see cref="UpdateFromPool"/> from a single
    /// logical updater (supervisor + flow-control monitor) or accept last-write wins on concurrent updates.</para>
    /// </remarks>
    public sealed class RabbitMqPoolHealth : IRabbitMqPoolHealth
    {
        /// <inheritdoc />
        public PoolHealthStatus Status { get; private set; } = PoolHealthStatus.Recovering;

        /// <summary>
        /// Recomputes <see cref="Status"/> from the current pool snapshot and records a health metric.
        /// </summary>
        /// <param name="pool">Connection pool whose <see cref="ConnectionPool.Snapshot"/> is evaluated.</param>
        /// <param name="options">Threshold configuration (<see cref="RabbitMQOptions.DegradedThreshold"/>,
        /// <see cref="RabbitMQOptions.UnhealthyThreshold"/>).</param>
        /// <remarks>
        /// <para><b>Algorithm:</b> Counts connections in <see cref="PooledConnectionState.Connected"/>, computes faulted
        /// fraction, maps to <see cref="PoolHealthStatus"/>, then calls <see cref="MessageBusMeters.RecordHealth(string)"/>.</para>
        /// </remarks>
        public void UpdateFromPool(ConnectionPool pool, RabbitMQOptions options)
        {
            int total = pool.Snapshot.Count;
            if (total == 0)
            {
                Status = PoolHealthStatus.Unhealthy;
                MessageBusMeters.RecordHealth("unhealthy");
                return;
            }
            int connected = pool.Snapshot.Count(c => c.State == PooledConnectionState.Connected);
            double faultedFraction = 1.0 - (connected / (double)total);
            Status = faultedFraction >= options.UnhealthyThreshold
                ? PoolHealthStatus.Unhealthy
                : faultedFraction >= options.DegradedThreshold
                    ? PoolHealthStatus.Degraded
                    : PoolHealthStatus.Healthy;
            MessageBusMeters.RecordHealth(Status.ToString().ToLowerInvariant());
        }
    }
}

