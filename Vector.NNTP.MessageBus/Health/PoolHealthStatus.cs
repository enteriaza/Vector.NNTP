// <copyright file="PoolHealthStatus.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// PoolHealthStatus.cs -- Bounded aggregate health states for the RabbitMQ connection pool.
//
// Values are persisted on IRabbitMqPoolHealth.Status and emitted to OpenTelemetry as lowercase strings.
// Numeric values are stable for logging and metrics cardinality control.

namespace Vector.NNTP.MessageBus.Health
{
    /// <summary>
    /// Aggregate health classification for the RabbitMQ <see cref="Connections.ConnectionPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Derivation:</b> <see cref="RabbitMqPoolHealth.UpdateFromPool"/> maps the fraction of faulted
    /// connections to these states using <see cref="Configuration.RabbitMQOptions.DegradedThreshold"/> and
    /// <see cref="Configuration.RabbitMQOptions.UnhealthyThreshold"/>.</para>
    ///
    /// <para><b>Readiness guidance:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="Healthy"/> — pool meets SLO; accept traffic.</description></item>
    ///   <item><description><see cref="Degraded"/> — elevated faults; traffic may continue with caution.</description></item>
    ///   <item><description><see cref="Recovering"/> — initial or transitional state before first health refresh.</description></item>
    ///   <item><description><see cref="Unhealthy"/> — insufficient connected capacity; fail readiness or shed load.</description></item>
    /// </list>
    /// </remarks>
    public enum PoolHealthStatus
    {
        /// <summary>
        /// Connected fraction meets healthy SLO; pool can serve publisher and consumer workloads at full capacity.
        /// </summary>
        Healthy = 0,

        /// <summary>
        /// Faulted fraction exceeds degraded threshold but remains below unhealthy; operational with reduced margin.
        /// </summary>
        Degraded = 1,

        /// <summary>
        /// Default before the first <see cref="IRabbitMqPoolHealth.UpdateFromPool"/> or while connections are still opening.
        /// </summary>
        Recovering = 2,

        /// <summary>
        /// Faulted fraction exceeds unhealthy threshold, or the pool snapshot is empty.
        /// </summary>
        Unhealthy = 3,

    }
}

