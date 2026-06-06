// <copyright file="HistoryMetricsTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics.Metrics;
using Vector.NNTP.HistoryDB.Metrics;

namespace Vector.NNTP.Tests.HistoryDB
{
    /// <summary>
    /// Verifies HistoryDB OpenTelemetry instruments and CHECK tier counter semantics.
    /// </summary>
    [TestFixture]
    public sealed class HistoryMetricsTests
    {
        /// <summary>
        /// Verifies observable gauges reflect memory and operational backing fields.
        /// </summary>
        [Test]
        public void ObservableGauges_ReflectSetters()
        {
            var metrics = new HistoryMetrics();
            var measurements = new Dictionary<string, long>(StringComparer.Ordinal);
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, meterListener) =>
                {
                    if (instrument.Meter.Name == "Vector.NNTP.HistoryDB" &&
                        instrument is ObservableInstrument<long>)
                    {
                        meterListener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                measurements[instrument.Name] = measurement;
            });

            listener.Start();

            metrics.SetMemoryEntries(42);
            metrics.SetMemoryBytes(3360);
            metrics.SetRebuildKeysProcessed(1_500_000);
            metrics.SetOperational(true);
            metrics.SetQueueDepth(7);

            listener.RecordObservableInstruments();

            Assert.That(measurements["history.memory.entries"], Is.EqualTo(42));
            Assert.That(measurements["history.memory.bytes"], Is.EqualTo(3360));
            Assert.That(measurements["history.rebuild.keys_processed"], Is.EqualTo(1_500_000));
            Assert.That(measurements["history.operational"], Is.EqualTo(1));
            Assert.That(measurements["history.queue.depth"], Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies terminal Duplicate and Wanted increment <c>history.check.total</c>.
        /// </summary>
        [Test]
        public void CheckTotal_IncrementsOnlyOnTerminalDuplicateOrWanted()
        {
            var metrics = new HistoryMetrics();
            var counters = new Dictionary<string, long>(StringComparer.Ordinal);
            using var listener = CreateCounterListener(counters);

            metrics.RecordDuplicate();
            metrics.RecordWanted();
            metrics.RecordTryAgain();
            metrics.RecordUnavailable();
            listener.RecordObservableInstruments();

            Assert.That(counters.GetValueOrDefault("history.check.total"), Is.EqualTo(2));
            Assert.That(counters.GetValueOrDefault("history.check.duplicate"), Is.EqualTo(1));
            Assert.That(counters.GetValueOrDefault("history.check.wanted"), Is.EqualTo(1));
            Assert.That(counters.GetValueOrDefault("history.check.try_again"), Is.EqualTo(1));
            Assert.That(counters.GetValueOrDefault("history.check.unavailable"), Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies tier counters for memory and Redis CHECK paths.
        /// </summary>
        [Test]
        public void TierCounters_MemoryAndRedisPaths()
        {
            var metrics = new HistoryMetrics();
            var counters = new Dictionary<string, long>(StringComparer.Ordinal);
            using var listener = CreateCounterListener(counters);

            metrics.RecordMemoryHit();
            metrics.RecordMemoryMiss();
            metrics.RecordRedisProbe();
            metrics.RecordRedisDuplicate();
            metrics.RecordRedisWanted();
            metrics.RecordSweepDeleted(3);
            metrics.RecordPersistFailure();
            metrics.RecordGenerationIoError();
            listener.RecordObservableInstruments();

            Assert.That(counters["history.check.memory_hit"], Is.EqualTo(1));
            Assert.That(counters["history.check.memory_miss"], Is.EqualTo(1));
            Assert.That(counters["history.check.redis_probe"], Is.EqualTo(1));
            Assert.That(counters["history.check.redis_duplicate"], Is.EqualTo(1));
            Assert.That(counters["history.check.redis_wanted"], Is.EqualTo(1));
            Assert.That(counters["history.rocks.sweep.deleted"], Is.EqualTo(3));
            Assert.That(counters["history.rocks.persist_failures"], Is.EqualTo(1));
            Assert.That(counters["history.generation.io_errors"], Is.EqualTo(1));
        }

        /// <summary>
        /// Creates a <see cref="MeterListener"/> that records HistoryDB counter increments.
        /// </summary>
        /// <param name="counters">Counter name to value map.</param>
        /// <returns>Started listener; dispose to detach.</returns>
        private static MeterListener CreateCounterListener(Dictionary<string, long> counters)
        {
            var listener = new MeterListener
            {
                InstrumentPublished = (instrument, meterListener) =>
                {
                    if (instrument.Meter.Name == "Vector.NNTP.HistoryDB" &&
                        instrument is Counter<long>)
                    {
                        meterListener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                counters[instrument.Name] = counters.GetValueOrDefault(instrument.Name) + measurement;
            });

            listener.Start();
            return listener;
        }
    }
}
