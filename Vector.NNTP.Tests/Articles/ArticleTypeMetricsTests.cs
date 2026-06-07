// <copyright file="ArticleTypeMetricsTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics.Metrics;
using Vector.NNTP.Articles.Classification;
using Vector.NNTP.Articles.Metrics;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="NntpSpoolMetrics.RecordArticleTypes"/>.
/// </summary>
[TestFixture]
public sealed class ArticleTypeMetricsTests
{
    /// <summary>
    /// Verifies multi-flag articles increment multiple <c>article_type_total</c> type tags.
    /// </summary>
    [Test]
    public void RecordArticleTypes_MultiFlag_IncrementsMultipleTags()
    {
        var metrics = new NntpSpoolMetrics();
        var measurements = new List<(string Tag, long Value)>();
        using MeterListener listener = CreateArticleTypeListener(measurements);

        metrics.RecordArticleTypes(ArticleTypeFlags.YEnc | ArticleTypeFlags.Binary);

        Assert.That(
            measurements,
            Is.EquivalentTo(
            [
                ("yenc", 1L),
                ("binary", 1L),
            ]));
    }

    /// <summary>
    /// Verifies unclassified articles increment the <c>default</c> tag once.
    /// </summary>
    [Test]
    public void RecordArticleTypes_Default_IncrementsDefaultTagOnce()
    {
        var metrics = new NntpSpoolMetrics();
        var measurements = new List<(string Tag, long Value)>();
        using MeterListener listener = CreateArticleTypeListener(measurements);

        metrics.RecordArticleTypes(ArticleTypeFlags.Default);

        Assert.That(measurements, Is.EquivalentTo([(ArticleTypeMetricsTags.DefaultTag, 1L)]));
    }

    /// <summary>
    /// Creates a <see cref="MeterListener"/> that captures <c>article_type_total</c> measurements.
    /// </summary>
    /// <param name="measurements">List populated with observed <c>type</c> tag values and measurement amounts.</param>
    /// <returns>A started listener; dispose after exercising metrics.</returns>
    private static MeterListener CreateArticleTypeListener(List<(string Tag, long Value)> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Vector.NNTP.Articles" &&
                    instrument.Name == "article_type_total")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            string? tag = null;
            foreach (KeyValuePair<string, object?> entry in tags)
            {
                if (entry.Key == "type" && entry.Value is string typeTag)
                {
                    tag = typeTag;
                    break;
                }
            }

            if (tag is not null)
            {
                measurements.Add((tag, measurement));
            }
        });

        listener.Start();
        return listener;
    }
}
