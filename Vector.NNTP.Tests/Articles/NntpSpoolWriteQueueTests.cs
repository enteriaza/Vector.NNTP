// <copyright file="NntpSpoolWriteQueueTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="NntpSpoolWriteQueue"/>.
/// </summary>
[TestFixture]
public sealed class NntpSpoolWriteQueueTests
{
    /// <summary>
    /// Verifies byte-budget rejection when the next article would exceed <see cref="NntpServerOptions.MaxQueuedBytes"/>.
    /// </summary>
    [Test]
    public void TryEnqueue_ExceedsByteBudget_ReturnsFalse()
    {
        var options = Options.Create(new NntpServerOptions
        {
            SpoolQueueCapacity = 16,
            MaxQueuedBytes = 128,
        });
        var queue = new NntpSpoolWriteQueue(options, new NntpSpoolMetrics());
        byte[] payload = new byte[100];
        Assert.That(queue.TryEnqueue(CreateItem("<a@b.c>", payload)), Is.True);
        Assert.That(queue.TryEnqueue(CreateItem("<b@c.d>", payload)), Is.False);
    }

    /// <summary>
    /// Verifies item-count rejection when capacity is reached.
    /// </summary>
    [Test]
    public void TryEnqueue_ExceedsItemCapacity_ReturnsFalse()
    {
        var options = Options.Create(new NntpServerOptions
        {
            SpoolQueueCapacity = 2,
            MaxQueuedBytes = 1_073_741_824,
        });
        var queue = new NntpSpoolWriteQueue(options, new NntpSpoolMetrics());
        byte[] small = [1];
        Assert.That(queue.TryEnqueue(CreateItem("<one@test>", small)), Is.True);
        Assert.That(queue.TryEnqueue(CreateItem("<two@test>", small)), Is.True);
        Assert.That(queue.TryEnqueue(CreateItem("<three@test>", small)), Is.False);
    }

    /// <summary>
    /// Verifies digest hex is attached to enqueued items.
    /// </summary>
    [Test]
    public void TryEnqueue_AttachesDigestHex()
    {
        const string messageId = "<digest@test.local>";
        var options = Options.Create(new NntpServerOptions());
        var queue = new NntpSpoolWriteQueue(options, new NntpSpoolMetrics());
        Assert.That(queue.TryEnqueue(CreateItem(messageId, [1, 2, 3])), Is.True);
        Assert.That(queue.Reader.TryRead(out NntpSpoolWriteItem? item), Is.True);
        Assert.That(item!.MessageIdDigestHex, Is.EqualTo(HistoryKeyEncoder.EncodeHexLower(messageId)));
    }

    /// <summary>
    /// Builds a queue item with digest hex for tests.
    /// </summary>
    /// <param name="messageId">Message identifier.</param>
    /// <param name="bytes">Article bytes.</param>
    /// <returns>Queue item.</returns>
    private static NntpSpoolWriteItem CreateItem(string messageId, byte[] bytes)
    {
        return new NntpSpoolWriteItem(
            messageId,
            bytes,
            HistoryKeyEncoder.EncodeHexLower(messageId),
            SpoolTestOrigins.SpoolOrigin());
    }
}
