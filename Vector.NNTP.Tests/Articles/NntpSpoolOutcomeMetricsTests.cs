// <copyright file="NntpSpoolOutcomeMetricsTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Storage;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for spool article outcome counters and minute snapshots on <see cref="NntpSpoolMetrics"/>.
/// </summary>
[TestFixture]
public sealed class NntpSpoolOutcomeMetricsTests
{
    /// <summary>
    /// Verifies accept/reject recording increments OpenTelemetry counters with feed and category tags.
    /// </summary>
    [Test]
    public void RecordArticleOutcomes_IncrementsOpenTelemetryCounters()
    {
        var metrics = new NntpSpoolMetrics();
        var accepted = new List<(string Feed, long Value)>();
        var rejected = new List<(string Feed, string Category, long Value)>();
        using MeterListener listener = CreateOutcomeListener(accepted, rejected);

        NntpSpoolArticleOrigin origin = CreateOrigin("Giganews");
        byte[] articleBytes = Encoding.ASCII.GetBytes("Path: peer.example.com\r\n\r\n");

        metrics.RecordArticleAccepted(origin, articleBytes);
        metrics.RecordArticleRejected(origin, articleBytes, SpoolArticleRejectionCategory.HeaderSyntax);
        metrics.RecordArticleRejected(origin, articleBytes, SpoolArticleRejectionCategory.Crc);

        Assert.That(accepted, Is.EquivalentTo([("Giganews", 1L)]));
        Assert.That(
            rejected,
            Is.EquivalentTo(
            [
                ("Giganews", SpoolArticleRejectionMetricsTags.HeaderSyntax, 1L),
                ("Giganews", SpoolArticleRejectionMetricsTags.Crc, 1L),
            ]));
    }

    /// <summary>
    /// Verifies minute snapshots return per-feed deltas and reset buckets.
    /// </summary>
    [Test]
    public void TakeMinuteSnapshotAndReset_ReturnsDeltasAndResetsBuckets()
    {
        var metrics = new NntpSpoolMetrics();
        NntpSpoolArticleOrigin giganews = CreateOrigin("Giganews");
        NntpSpoolArticleOrigin local = CreateOrigin(isLocalPost: true);
        byte[] articleBytes = Encoding.ASCII.GetBytes("Path: peer.example.com\r\n\r\n");

        metrics.RecordArticleAccepted(giganews, articleBytes);
        metrics.RecordArticleRejected(giganews, articleBytes, SpoolArticleRejectionCategory.Crosspost);
        metrics.RecordArticleRejected(giganews, articleBytes, SpoolArticleRejectionCategory.Other);
        metrics.RecordArticleAccepted(local, ReadOnlySpan<byte>.Empty);

        SpoolThroughputMinuteSnapshot snapshot = metrics.TakeMinuteSnapshotAndReset();

        Assert.That(snapshot.Global.Processed, Is.EqualTo(4));
        Assert.That(snapshot.Global.Accepted, Is.EqualTo(2));
        Assert.That(snapshot.Global.Rejected, Is.EqualTo(2));
        Assert.That(snapshot.Global.Crosspost, Is.EqualTo(1));
        Assert.That(snapshot.Global.Other, Is.EqualTo(1));
        Assert.That(snapshot.Feeds, Has.Count.EqualTo(2));
        Assert.That(snapshot.Feeds[0].Feed, Is.EqualTo("Giganews"));
        Assert.That(snapshot.Feeds[1].Feed, Is.EqualTo("local"));

        SpoolThroughputMinuteSnapshot emptySnapshot = metrics.TakeMinuteSnapshotAndReset();
        Assert.That(emptySnapshot.Global.Processed, Is.EqualTo(0));
        Assert.That(emptySnapshot.Feeds, Is.Empty);
    }

    /// <summary>
    /// Builds a spool origin for metrics tests.
    /// </summary>
    /// <param name="transitPeerName">Configured transit peer name.</param>
    /// <param name="isLocalPost">When true, marks the origin as a local POST.</param>
    /// <returns>Origin snapshot for feed resolution.</returns>
    private static NntpSpoolArticleOrigin CreateOrigin(
        string? transitPeerName = null,
        bool isLocalPost = false)
    {
        return new NntpSpoolArticleOrigin(
            IPAddress.Loopback,
            PeerHostName: null,
            ReceivedUtc: DateTimeOffset.UtcNow,
            TransitPeerName: transitPeerName,
            IsLocalPost: isLocalPost);
    }

    /// <summary>
    /// Creates a <see cref="MeterListener"/> that captures article outcome counter measurements.
    /// </summary>
    /// <param name="accepted">Accepted counter observations.</param>
    /// <param name="rejected">Rejected counter observations.</param>
    /// <returns>A started listener; dispose after exercising metrics.</returns>
    private static MeterListener CreateOutcomeListener(
        List<(string Feed, long Value)> accepted,
        List<(string Feed, string Category, long Value)> rejected)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name != "Vector.NNTP.Articles")
                {
                    return;
                }

                if (instrument.Name is "nntp.spool.article.accepted" or "nntp.spool.article.rejected")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            string? feed = null;
            string? category = null;
            foreach (KeyValuePair<string, object?> entry in tags)
            {
                if (entry.Key == "feed" && entry.Value is string feedTag)
                {
                    feed = feedTag;
                }
                else if (entry.Key == "category" && entry.Value is string categoryTag)
                {
                    category = categoryTag;
                }
            }

            if (feed is null)
            {
                return;
            }

            if (instrument.Name == "nntp.spool.article.accepted")
            {
                accepted.Add((feed, measurement));
            }
            else if (category is not null)
            {
                rejected.Add((feed, category, measurement));
            }
        });

        listener.Start();
        return listener;
    }
}
