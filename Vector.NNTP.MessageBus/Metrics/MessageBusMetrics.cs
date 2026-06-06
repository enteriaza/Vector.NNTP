// <copyright file="MessageBusMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// MessageBusMetrics.cs -- Instance-backed OpenTelemetry counters for MessageBus throughput and reliability signals.

using System.Diagnostics.Metrics;

namespace Vector.NNTP.MessageBus.Metrics
{
    /// <summary>
    /// Holds OpenTelemetry instruments used by MessageBus publishers, consumers, and health tracking.
    /// </summary>
    /// <remarks>
    /// <para>This type is registered as a singleton so hot-path components can increment counters without static globals.</para>
    /// <para>Label values are bounded to prevent metric cardinality growth in long-running servers.</para>
    /// </remarks>
    internal sealed class MessageBusMetrics
    {
        /// <summary>Process-wide meter for MessageBus instruments.</summary>
        private readonly Meter _meter = new("MessageBus", "1.0.0");

        /// <summary>Counts successful publisher slot acquisitions.</summary>
        private readonly Counter<long> _slotAcquireCounter;

        /// <summary>Counts successful broker-confirmed publishes.</summary>
        private readonly Counter<long> _publishCounter;

        /// <summary>Counts publish operations that exceeded confirm timeout.</summary>
        private readonly Counter<long> _confirmTimeoutCounter;

        /// <summary>Counts handled publish failures classified by category.</summary>
        private readonly Counter<long> _publishFailureCounter;

        /// <summary>Counts consumer delivery handler failures by category.</summary>
        private readonly Counter<long> _deliveryFailureCounter;

        /// <summary>Counts health status transitions.</summary>
        private readonly Counter<long> _healthCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageBusMetrics"/> class.
        /// </summary>
        public MessageBusMetrics()
        {
            _slotAcquireCounter = _meter.CreateCounter<long>("messagebus.publisher.slots.acquired");
            _publishCounter = _meter.CreateCounter<long>("messagebus.publisher.publishes");
            _confirmTimeoutCounter = _meter.CreateCounter<long>("messagebus.publisher.confirm_timeouts");
            _publishFailureCounter = _meter.CreateCounter<long>("messagebus.publisher.failures");
            _deliveryFailureCounter = _meter.CreateCounter<long>("messagebus.consumer.delivery.failures");
            _healthCounter = _meter.CreateCounter<long>("messagebus.pool.health_transitions");
        }

        /// <summary>Records a successful publisher slot acquisition.</summary>
        public void RecordSlotAcquired()
        {
            _slotAcquireCounter.Add(1);
        }

        /// <summary>Records a successful publish confirmation.</summary>
        public void RecordPublish()
        {
            _publishCounter.Add(1);
        }

        /// <summary>Records a publish confirm timeout.</summary>
        public void RecordConfirmTimeout()
        {
            _confirmTimeoutCounter.Add(1);
        }

        /// <summary>
        /// Records a classified publish failure.
        /// </summary>
        /// <param name="classification">Bounded failure class label.</param>
        public void RecordPublishFailure(string classification)
        {
            _publishFailureCounter.Add(1, new KeyValuePair<string, object?>("class", classification));
        }

        /// <summary>
        /// Records a classified consumer delivery handler failure.
        /// </summary>
        /// <param name="classification">Bounded failure class label.</param>
        public void RecordDeliveryFailure(string classification)
        {
            _deliveryFailureCounter.Add(1, new KeyValuePair<string, object?>("class", classification));
        }

        /// <summary>
        /// Records a health transition with bounded status labels.
        /// </summary>
        /// <param name="healthStatus">Lowercase health state label.</param>
        public void RecordHealth(string healthStatus)
        {
            _healthCounter.Add(1, new KeyValuePair<string, object?>("health_status", healthStatus));
        }
    }
}
