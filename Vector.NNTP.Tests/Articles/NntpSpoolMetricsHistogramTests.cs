// <copyright file="NntpSpoolMetricsHistogramTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics.Metrics;
using Vector.NNTP.Articles.Metrics;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for spool latency histogram and auxiliary counter instruments on <see cref="NntpSpoolMetrics"/>.
/// </summary>
[TestFixture]
public sealed class NntpSpoolMetricsHistogramTests
{
    /// <summary>
    /// Verifies preprocess/postprocess/write/spamd duration histograms record positive samples.
    /// </summary>
    [Test]
    public void RecordStageDurations_RecordsHistogramMeasurements()
    {
        var metrics = new NntpSpoolMetrics();
        var durations = new Dictionary<string, List<double>>();
        using MeterListener listener = CreateDurationListener(durations);

        metrics.RecordPreprocessDuration(12.5);
        metrics.RecordPostprocessDuration(34.0);
        metrics.RecordWriteDuration(8.25);
        metrics.RecordSpamdDuration(55.0);

        Assert.That(durations["nntp.spool.preprocess.duration_ms"], Is.EqualTo([12.5]));
        Assert.That(durations["nntp.spool.postprocess.duration_ms"], Is.EqualTo([34.0]));
        Assert.That(durations["nntp.spool.write.duration_ms"], Is.EqualTo([8.25]));
        Assert.That(durations["nntp.spool.spamd.duration_ms"], Is.EqualTo([55.0]));
    }

    /// <summary>
    /// Verifies fail-open and writer scale counters record tagged measurements.
    /// </summary>
    [Test]
    public void RecordSpamdFailOpenAndWriterScale_RecordsTaggedCounters()
    {
        var metrics = new NntpSpoolMetrics();
        var spamdFailOpen = new List<(string Reason, long Value)>();
        var writerScale = new List<(string Direction, long Value)>();
        using MeterListener listener = CreateTaggedCounterListener(spamdFailOpen, writerScale);

        metrics.RecordSpamdFailOpen("protocol");
        metrics.RecordWriterScale("up");
        metrics.RecordWriterScale("down");

        Assert.That(spamdFailOpen, Is.EquivalentTo([("protocol", 1L)]));
        Assert.That(
            writerScale,
            Is.EquivalentTo(
            [
                ("up", 1L),
                ("down", 1L),
            ]));
    }

    /// <summary>
    /// Creates a listener that captures spool duration histogram measurements.
    /// </summary>
    /// <param name="durations">Captured duration samples keyed by instrument name.</param>
    /// <returns>A started listener; dispose after exercising metrics.</returns>
    private static MeterListener CreateDurationListener(Dictionary<string, List<double>> durations)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name != "Vector.NNTP.Articles")
                {
                    return;
                }

                if (instrument.Name.EndsWith(".duration_ms", StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            if (!durations.TryGetValue(instrument.Name, out List<double>? samples))
            {
                samples = [];
                durations[instrument.Name] = samples;
            }

            samples.Add(measurement);
        });

        listener.Start();
        return listener;
    }

    /// <summary>
    /// Creates a listener that captures tagged spool counter measurements.
    /// </summary>
    /// <param name="spamdFailOpen">Captured spamd fail-open counter observations.</param>
    /// <param name="writerScale">Captured writer scale counter observations.</param>
    /// <returns>A started listener; dispose after exercising metrics.</returns>
    private static MeterListener CreateTaggedCounterListener(
        List<(string Reason, long Value)> spamdFailOpen,
        List<(string Direction, long Value)> writerScale)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name != "Vector.NNTP.Articles")
                {
                    return;
                }

                if (instrument.Name is "nntp.spool.spamd.fail_open" or "nntp.spool.writers.scale_total")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            string? tagValue = null;
            foreach (KeyValuePair<string, object?> entry in tags)
            {
                if (entry.Value is string text)
                {
                    tagValue = text;
                    break;
                }
            }

            if (tagValue is null)
            {
                return;
            }

            if (instrument.Name == "nntp.spool.spamd.fail_open")
            {
                spamdFailOpen.Add((tagValue, measurement));
            }
            else
            {
                writerScale.Add((tagValue, measurement));
            }
        });

        listener.Start();
        return listener;
    }
}
