// <copyright file="MessageBusMeters.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusMeters.cs -- OpenTelemetry counters for MessageBus pool, publish, and health signals.
//
// Centralises metric names and instruments so hot paths call static Record* methods instead of scattering
// Meter.CreateCounter at every call site. Labels are bounded to avoid cardinality explosions in Prometheus.
//
// Thread safety:
//   System.Diagnostics.Metrics counters are thread-safe; static initialisation is idempotent.
//
// Logging:
//   Not applicable — metrics only; no ILogger dependency.

using System.Diagnostics.Metrics;

namespace Vector.NNTP.MessageBus.Metrics
{
    /// <summary>
    /// OpenTelemetry <see cref="Meter"/> instruments for MessageBus with bounded cardinality labels.
    /// </summary>
    /// <remarks>
    /// <para><b>Rationale:</b> Publisher pools, scopes, and health aggregators increment counters on hot paths.
    /// This type owns instrument creation once at class load and exposes narrow <c>Record*</c> helpers.</para>
    ///
    /// <para><b>Cardinality:</b> <see cref="RecordHealth(string)"/> accepts only normalised status strings produced by
    /// <see cref="Health.RabbitMqPoolHealth"/> (<c>healthy</c>, <c>degraded</c>, <c>unhealthy</c>) — callers must not
    /// pass unbounded queue or host names as labels.</para>
    ///
    /// <para><b>Allocation:</b> Each <c>Record*</c> call is a single counter increment; instrument handles are cached in
    /// <see langword="static readonly"/> fields.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/>; BCL counters are safe under concurrent
    /// publisher and hosted-service threads.</para>
    /// </remarks>
    public static class MessageBusMeters
    {
        /// <summary>Process-wide meter named <c>MessageBus</c> at version <c>1.0.0</c>.</summary>
        private static readonly Meter Meter = new("MessageBus", "1.0.0");

        /// <summary>Increments when <see cref="Publishing.RabbitMqPublisherPool"/> successfully acquires a publisher slot.</summary>
        private static readonly Counter<long> SlotAcquireCounter = Meter.CreateCounter<long>("messagebus.publisher.slots.acquired");

        /// <summary>Increments when <see cref="Publishing.RabbitMqPublisherScope.PublishAsync"/> completes after broker confirm.</summary>
        private static readonly Counter<long> PublishCounter = Meter.CreateCounter<long>("messagebus.publisher.publishes");

        /// <summary>Increments when publisher confirm wait exceeds <see cref="Configuration.RabbitMQOptions.PublishConfirmTimeout"/>.</summary>
        private static readonly Counter<long> ConfirmTimeoutCounter = Meter.CreateCounter<long>("messagebus.publisher.confirm_timeouts");

        /// <summary>Reserved for suppressed diagnostic events when logging sinks fail (not yet wired from call sites).</summary>
        private static readonly Counter<long> SuppressedLogCounter = Meter.CreateCounter<long>("messagebus.logging.suppressed");

        /// <summary>Increments on each <see cref="Health.RabbitMqPoolHealth.UpdateFromPool"/> health classification.</summary>
        private static readonly Counter<long> HealthCounter = Meter.CreateCounter<long>("messagebus.pool.health_transitions");

        /// <summary>Records a successful publisher slot acquisition.</summary>
        /// <remarks>Called from <see cref="Publishing.RabbitMqPublisherPool.CreateScopeAsync"/> after channel creation.</remarks>
        public static void RecordSlotAcquired()
        {
            SlotAcquireCounter.Add(1);
        }

        /// <summary>Records a publish that received broker publisher confirmation.</summary>
        /// <remarks>Called from <see cref="Publishing.RabbitMqPublisherScope.PublishAsync"/> on successful await of
        /// <see cref="RabbitMQ.Client.IChannel.BasicPublishAsync"/>.</remarks>
        public static void RecordPublish()
        {
            PublishCounter.Add(1);
        }

        /// <summary>Records a publish confirm timeout (linked CTS fired before caller cancellation).</summary>
        /// <remarks>Called from <see cref="Publishing.RabbitMqPublisherScope.PublishAsync"/> when confirm wait is exceeded.</remarks>
        public static void RecordConfirmTimeout()
        {
            ConfirmTimeoutCounter.Add(1);
        }

        /// <summary>Records a suppressed log event when the logging pipeline cannot emit diagnostics.</summary>
        /// <remarks>Reserved for parity with connection-factory swallowed-handler counters; call when intentionally
        /// dropping log output to protect hot paths.</remarks>
        public static void RecordSuppressedLog()
        {
            SuppressedLogCounter.Add(1);
        }

        /// <summary>Records a pool health transition with a bounded status label.</summary>
        /// <param name="healthStatus">Lowercase status token (<c>healthy</c>, <c>degraded</c>, <c>unhealthy</c>).</param>
        /// <remarks>Must not pass host names, queue names, or exception text — only enumerated health states.</remarks>
        public static void RecordHealth(string healthStatus)
        {
            HealthCounter.Add(1, new KeyValuePair<string, object?>("health_status", healthStatus));
        }
    }
}

