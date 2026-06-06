// <copyright file="IRabbitMqPoolHealth.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// IRabbitMqPoolHealth.cs -- Read-only contract for aggregate RabbitMQ pool health exposed to hosts and probes.
//
// Implemented by RabbitMqPoolHealth and updated when the connection snapshot changes. Hosts may inject this
// interface for readiness checks without referencing ConnectionPool internals.
//
// Thread safety:
//   Implementations may update Status without external locking; readers observe last-written values.

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Connections;

namespace Vector.NNTP.MessageBus.Health
{
    /// <summary>
    /// Read-only health surface for the RabbitMQ <see cref="ConnectionPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Consumers:</b> Host readiness probes and operational dashboards. Updated by
    /// <see cref="RabbitMqPoolSupervisor"/> at startup and <see cref="RabbitMqPoolFlowControlMonitor"/> after each
    /// flow-control scan.</para>
    ///
    /// <para><b>Stability:</b> <see cref="Status"/> is a snapshot at the time of the last
    /// <see cref="UpdateFromPool"/> — not a live stream of per-connection events.</para>
    /// </remarks>
    internal interface IRabbitMqPoolHealth
    {
        /// <summary>
        /// Current aggregate status after the most recent <see cref="UpdateFromPool"/>.
        /// </summary>
        public PoolHealthStatus Status { get; }

        /// <summary>
        /// Recomputes aggregate status from the supplied pool snapshot.
        /// </summary>
        /// <param name="pool">Connection pool to inspect.</param>
        /// <param name="options">Threshold configuration for degraded and unhealthy transitions.</param>
        /// <remarks>
        /// <para><b>Caller responsibility:</b> Invoke after snapshot mutations that affect connection state counts
        /// (startup, quarantine, reconnect) so <see cref="Status"/> reflects operational reality.</para>
        /// </remarks>
        public void UpdateFromPool(ConnectionPool pool, RabbitMQOptions options);
    }
}

