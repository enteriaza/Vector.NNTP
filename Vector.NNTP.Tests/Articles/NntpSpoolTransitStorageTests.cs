// <copyright file="NntpSpoolTransitStorageTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="NntpSpoolTransitStorage"/>.
/// </summary>
[TestFixture]
public sealed class NntpSpoolTransitStorageTests
{
    /// <summary>
    /// Verifies a non-empty article is enqueued and returns <see cref="NntpTransitStorageResult.Success"/>.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task TakeThisAsync_ValidArticle_ReturnsSuccess()
    {
        NntpSpoolTransitStorage storage = CreateStorage(capacity: 4, maxQueuedBytes: 4096);
        NntpTransitStorageResult result = await storage.TakeThisAsync(
            "<valid@test.local>",
            MinimalArticleBytes(),
            SpoolTestOrigins.TransitOrigin(),
            CancellationToken.None).ConfigureAwait(false);

        Assert.That(result, Is.EqualTo(NntpTransitStorageResult.Success));
    }

    /// <summary>
    /// Verifies queue-capacity rejection returns <see cref="NntpTransitStorageResult.QueueFull"/>.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task TakeThisAsync_QueueFull_ReturnsQueueFull()
    {
        NntpSpoolTransitStorage storage = CreateStorage(capacity: 1, maxQueuedBytes: 4096);
        byte[] payload = MinimalArticleBytes();

        Assert.That(
            await storage.TakeThisAsync("<one@test.local>", payload, SpoolTestOrigins.TransitOrigin(), CancellationToken.None).ConfigureAwait(false),
            Is.EqualTo(NntpTransitStorageResult.Success));
        Assert.That(
            await storage.TakeThisAsync("<two@test.local>", payload, SpoolTestOrigins.TransitOrigin(), CancellationToken.None).ConfigureAwait(false),
            Is.EqualTo(NntpTransitStorageResult.QueueFull));
    }

    /// <summary>
    /// Verifies payloads exceeding <see cref="NntpServerOptions.MaxArtSize"/> return
    /// <see cref="NntpTransitStorageResult.ArticleRejected"/> without enqueueing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task TakeThisAsync_ExceedsMaxArtSize_ReturnsArticleRejectedWithoutEnqueueing()
    {
        const int maxArtSize = 64;
        NntpSpoolWriteQueue queue = CreateQueue(capacity: 4, maxQueuedBytes: 4096);
        NntpSpoolTransitStorage storage = CreateStorage(queue, maxArtSize: maxArtSize);
        byte[] oversized = new byte[maxArtSize + 1];

        NntpTransitStorageResult result = await storage.TakeThisAsync(
            "<oversize@test.local>",
            oversized,
            SpoolTestOrigins.TransitOrigin(),
            CancellationToken.None).ConfigureAwait(false);

        Assert.That(result, Is.EqualTo(NntpTransitStorageResult.ArticleRejected));
        Assert.That(queue.Depth, Is.Zero);
        Assert.That(queue.QueuedBytes, Is.Zero);
        Assert.That(queue.Reader.TryRead(out _), Is.False);
    }

    /// <summary>
    /// Verifies payloads exactly at <see cref="NntpServerOptions.MaxArtSize"/> are accepted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task TakeThisAsync_AtMaxArtSize_ReturnsSuccess()
    {
        const int maxArtSize = 64;
        NntpSpoolTransitStorage storage = CreateStorage(capacity: 4, maxQueuedBytes: 4096, maxArtSize: maxArtSize);
        byte[] payload = new byte[maxArtSize];

        Assert.That(
            await storage.TakeThisAsync("<limit@test.local>", payload, SpoolTestOrigins.TransitOrigin(), CancellationToken.None).ConfigureAwait(false),
            Is.EqualTo(NntpTransitStorageResult.Success));
    }

    /// <summary>
    /// Verifies <see cref="NntpServerOptions.MaxArtSize"/> of zero disables the storage-layer size check.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task TakeThisAsync_MaxArtSizeZero_DisablesLimit()
    {
        NntpSpoolTransitStorage storage = CreateStorage(capacity: 4, maxQueuedBytes: 4096, maxArtSize: 0);
        byte[] payload = new byte[256];

        Assert.That(
            await storage.TakeThisAsync("<unlimited@test.local>", payload, SpoolTestOrigins.TransitOrigin(), CancellationToken.None).ConfigureAwait(false),
            Is.EqualTo(NntpTransitStorageResult.Success));
    }

    /// <summary>
    /// Verifies enqueued items carry the expected digest hex.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task TakeThisAsync_AttachesDigestHex()
    {
        const string messageId = "<digest@test.local>";
        NntpSpoolWriteQueue queue = CreateQueue(capacity: 4, maxQueuedBytes: 4096);
        NntpSpoolTransitStorage storage = CreateStorage(queue);

        Assert.That(
            await storage.TakeThisAsync(messageId, MinimalArticleBytes(), SpoolTestOrigins.TransitOrigin(), CancellationToken.None).ConfigureAwait(false),
            Is.EqualTo(NntpTransitStorageResult.Success));
        Assert.That(queue.Reader.TryRead(out NntpSpoolWriteItem? item), Is.True);
        Assert.That(item!.MessageIdDigestHex, Is.EqualTo(HistoryKeyEncoder.EncodeHexLower(messageId)));
    }

    /// <summary>
    /// Builds transit storage backed by a queue with the given limits.
    /// </summary>
    /// <param name="capacity">Maximum queued item count.</param>
    /// <param name="maxQueuedBytes">Maximum queued payload bytes.</param>
    /// <param name="maxArtSize">Maximum decoded article size; zero disables the limit.</param>
    /// <returns>Configured storage instance.</returns>
    private static NntpSpoolTransitStorage CreateStorage(
        int capacity,
        long maxQueuedBytes,
        long maxArtSize = 1_048_576)
    {
        return CreateStorage(CreateQueue(capacity, maxQueuedBytes), maxArtSize);
    }

    /// <summary>
    /// Builds transit storage for an existing queue and article size limit.
    /// </summary>
    /// <param name="queue">Configured spool write queue.</param>
    /// <param name="maxArtSize">Maximum decoded article size; zero disables the limit.</param>
    /// <returns>Configured storage instance.</returns>
    private static NntpSpoolTransitStorage CreateStorage(NntpSpoolWriteQueue queue, long maxArtSize = 1_048_576)
    {
        var options = Options.Create(new NntpServerOptions
        {
            MaxArtSize = maxArtSize,
        });
        return new NntpSpoolTransitStorage(queue, options);
    }

    /// <summary>
    /// Builds a spool write queue with the given limits.
    /// </summary>
    /// <param name="capacity">Maximum queued item count.</param>
    /// <param name="maxQueuedBytes">Maximum queued payload bytes.</param>
    /// <returns>Configured queue instance.</returns>
    private static NntpSpoolWriteQueue CreateQueue(int capacity, long maxQueuedBytes)
    {
        var options = Options.Create(new NntpServerOptions
        {
            SpoolQueueCapacity = capacity,
            MaxQueuedBytes = maxQueuedBytes,
        });
        return new NntpSpoolWriteQueue(options, new NntpSpoolMetrics());
    }

    /// <summary>
    /// Returns minimal non-empty article bytes (headers and blank separator, no body).
    /// </summary>
    /// <returns>Header-only article payload.</returns>
    private static byte[] MinimalArticleBytes()
    {
        return "Path: misc.test\r\nMessage-ID: <a@b>\r\n\r\n"u8.ToArray();
    }
}
