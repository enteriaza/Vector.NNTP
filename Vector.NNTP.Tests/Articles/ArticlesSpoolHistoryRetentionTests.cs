// <copyright file="ArticlesSpoolHistoryRetentionTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Vector.NNTP.Articles.Diagnostics;
using Vector.NNTP.Articles.Logging;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Filters.PostFilter;
using Vector.NNTP.Filters.SpamAssassin;
using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.HistoryDB.Configuration;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.HistoryDB.Metrics;
using Vector.NNTP.HistoryDB.Redis;
using Vector.NNTP.HistoryDB.Rocks;
using Vector.NNTP.HistoryDB.Services;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Tests.HistoryDB;
using Vector.NNTP.Tests.HistoryDB.Fakes;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Verifies HistoryDB retention and release across the Articles spool writer pipeline.
/// </summary>
[TestFixture]
public sealed class ArticlesSpoolHistoryRetentionTests
{
    private const string RedisKeyPrefix = "test:articles:history:";

    private static bool _redisAvailable;
    private static string _skipReason = "Redis not reachable on localhost:6379.";

    private string? _spoolDirectory;
    private string? _historyDbDir;

    /// <summary>
    /// Probes Redis once for the fixture.
    /// </summary>
    [OneTimeSetUp]
    public void ProbeRedis()
    {
        try
        {
            using ConnectionMultiplexer mux = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false,connectTimeout=2000");
            _ = mux.GetDatabase().Ping();
            _redisAvailable = true;
        }
        catch (Exception ex)
        {
            _redisAvailable = false;
            _skipReason = ex.Message;
        }
    }

    /// <summary>
    /// Creates isolated spool and Rocks directories before each test.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        string suffix = Guid.NewGuid().ToString("N");
        this._spoolDirectory = Path.Combine(Path.GetTempPath(), "vector-nntp-history-retention-spool-" + suffix);
        this._historyDbDir = Path.Combine(Path.GetTempPath(), "vector-nntp-history-retention-rocks-" + suffix);
        Directory.CreateDirectory(this._spoolDirectory);
        Directory.CreateDirectory(this._historyDbDir);
    }

    /// <summary>
    /// Deletes temporary directories after each test.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (this._spoolDirectory is not null && Directory.Exists(this._spoolDirectory))
        {
            Directory.Delete(this._spoolDirectory, recursive: true);
        }

        if (this._historyDbDir is not null && Directory.Exists(this._historyDbDir))
        {
            Directory.Delete(this._historyDbDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies TAKETHIS record plus successful spool write leaves history in all tiers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    [Category("Redis")]
    public async Task TakethisRecordThenSpoolSuccess_RetainsHistoryInAllTiers()
    {
        if (!_redisAvailable)
        {
            Assert.Ignore(_skipReason);
        }

        const string messageId = "<retained@example.com>";
        using ConnectionMultiplexer multiplexer = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
        var accessor = new HistoryRedisTestAccessor(multiplexer);
        using var rocks = CreateHistoryService(this._historyDbDir!, accessor, out HistoryDatabaseService history);

        HistoryRecordResult record = await history.TryRecordAsync(messageId, CancellationToken.None).ConfigureAwait(false);
        Assert.That(record, Is.EqualTo(HistoryRecordResult.Recorded));

        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) = CreatePump(history, this._spoolDirectory!);
        byte[] article = BuildValidArticle(messageId);
        await RunPumpOnceAsync(pump, queue, CreateItem(messageId, article)).ConfigureAwait(false);

        HistoryCheckResult check = await history.CheckAsync(messageId, CancellationToken.None).ConfigureAwait(false);
        Assert.That(check, Is.EqualTo(HistoryCheckResult.Duplicate));

        byte[] digest = new byte[HistoryKeyEncoder.DigestLength];
        Assert.That(HistoryKeyEncoder.TryComputeDigest(messageId, digest), Is.True);
        await WaitForRocksPersistAsync(rocks, digest).ConfigureAwait(false);
        Assert.That(rocks.GetDigestExpiration(digest), Is.Not.Null);
    }

    /// <summary>
    /// Verifies spool commit records history even when TAKETHIS did not call <see cref="IHistoryDatabase.TryRecordAsync"/>.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    [Category("Redis")]
    public async Task SpoolSuccessWithoutPriorRecord_CommitsHistoryOnWrite()
    {
        if (!_redisAvailable)
        {
            Assert.Ignore(_skipReason);
        }

        const string messageId = "<commit-on-write@example.com>";
        using ConnectionMultiplexer multiplexer = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
        var accessor = new HistoryRedisTestAccessor(multiplexer);
        using var rocks = CreateHistoryService(this._historyDbDir!, accessor, out HistoryDatabaseService history);

        HistoryCheckResult wantedBefore = await history.CheckAsync(messageId, CancellationToken.None).ConfigureAwait(false);
        Assert.That(wantedBefore, Is.EqualTo(HistoryCheckResult.Wanted));

        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) = CreatePump(history, this._spoolDirectory!);
        byte[] article = BuildValidArticle(messageId);
        await RunPumpOnceAsync(pump, queue, CreateItem(messageId, article)).ConfigureAwait(false);

        HistoryCheckResult check = await history.CheckAsync(messageId, CancellationToken.None).ConfigureAwait(false);
        Assert.That(check, Is.EqualTo(HistoryCheckResult.Duplicate));
    }

    /// <summary>
    /// Verifies postprocess failure releases a prior TAKETHIS reservation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    [Category("Redis")]
    public async Task SpoolPostprocessFailure_ReleasesPriorTakethisRecord()
    {
        if (!_redisAvailable)
        {
            Assert.Ignore(_skipReason);
        }

        const string messageId = "<released@example.com>";
        using ConnectionMultiplexer multiplexer = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
        var accessor = new HistoryRedisTestAccessor(multiplexer);
        using var rocks = CreateHistoryService(this._historyDbDir!, accessor, out HistoryDatabaseService history);

        HistoryRecordResult record = await history.TryRecordAsync(messageId, CancellationToken.None).ConfigureAwait(false);
        Assert.That(record, Is.EqualTo(HistoryRecordResult.Recorded));

        var spamAssassin = new FailOpenSpamAssassin { IsSpam = true };
        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) = CreatePump(history, this._spoolDirectory!, spamAssassin);
        byte[] article = BuildValidArticle(messageId);
        await RunPumpOnceAsync(pump, queue, CreateItem(messageId, article)).ConfigureAwait(false);

        HistoryCheckResult check = await history.CheckAsync(messageId, CancellationToken.None).ConfigureAwait(false);
        Assert.That(check, Is.EqualTo(HistoryCheckResult.Wanted));
        _ = rocks;
    }

    /// <summary>
    /// Verifies the pump calls <see cref="IHistoryDatabase.TryRecordAsync"/> after a successful spool write.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task SpoolSuccess_InvokesHistoryCommitAfterWrite()
    {
        var history = new TrackingHistoryDatabase();
        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) = CreatePump(history, this._spoolDirectory!);
        const string messageId = "<tracked@example.com>";
        byte[] article = BuildValidArticle(messageId);

        await RunPumpOnceAsync(pump, queue, CreateItem(messageId, article)).ConfigureAwait(false);

        Assert.That(history.RecordCount, Is.EqualTo(1));
        Assert.That(history.ReleaseCount, Is.EqualTo(0));
        Assert.That(history.LastRecordedMessageId, Is.EqualTo(messageId));
    }

    /// <summary>
    /// Verifies the pump calls <see cref="IHistoryDatabase.TryReleaseAsync"/> after postprocess failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task SpoolPostprocessFailure_InvokesHistoryRelease()
    {
        var history = new TrackingHistoryDatabase();
        history.SeedDuplicate("<spam@example.com>");
        var spamAssassin = new FailOpenSpamAssassin { IsSpam = true };
        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) =
            CreatePump(history, this._spoolDirectory!, spamAssassin);
        const string messageId = "<spam@example.com>";
        byte[] article = BuildValidArticle(messageId);

        await RunPumpOnceAsync(pump, queue, CreateItem(messageId, article)).ConfigureAwait(false);

        Assert.That(history.ReleaseCount, Is.EqualTo(1));
        Assert.That(history.LastReleasedMessageId, Is.EqualTo(messageId));
    }

    /// <summary>
    /// Builds a pump and queue wired to the supplied history database.
    /// </summary>
    /// <param name="historyDatabase">History database under test.</param>
    /// <param name="spoolDirectory">Spool root directory.</param>
    /// <param name="spamAssassin">Optional spamd client override.</param>
    /// <returns>Pump and queue tuple.</returns>
    private static (NntpSpoolWriterPump Pump, NntpSpoolWriteQueue Queue) CreatePump(
        IHistoryDatabase historyDatabase,
        string spoolDirectory,
        ISpamAssassin? spamAssassin = null)
    {
        var queueOptions = Options.Create(new NntpServerOptions
        {
            SpoolQueueCapacity = 4,
            MaxQueuedBytes = 4_194_304,
            SpoolDir = spoolDirectory,
        });
        var serverOptions = Options.Create(new NntpServerOptions
        {
            NodeName = "transit1",
            DomainName = "usenetninja.net",
            MaxArtSize = 4_194_304,
            SpoolDir = spoolDirectory,
        });
        var queue = new NntpSpoolWriteQueue(queueOptions, new NntpSpoolMetrics());
        var preprocessor = new ArticleSpoolPreprocessor(serverOptions);
        var postprocessor = new ArticleSpoolPostprocessor(
            Options.Create(new PostFilterOptions()),
            serverOptions,
            spamAssassin ?? new FailOpenSpamAssassin(),
            new SpamdScanArticleBuilder(),
            NullLogger<ArticleSpoolPostprocessor>.Instance);
        var pump = new NntpSpoolWriterPump(
            queue,
            preprocessor,
            postprocessor,
            historyDatabase,
            new NntpSpoolMetrics(),
            serverOptions,
            NullNntpNewsLog.Instance,
            NullLogger<NntpSpoolWriterPump>.Instance);

        return (pump, queue);
    }

    /// <summary>
    /// Builds an operational history stack backed by Redis and RocksDB.
    /// </summary>
    /// <param name="historyDbDir">RocksDB directory.</param>
    /// <param name="accessor">Redis accessor.</param>
    /// <param name="history">Created history service.</param>
    /// <returns>Open Rocks store disposed by the caller.</returns>
    private static RocksHistoryStore CreateHistoryService(
        string historyDbDir,
        HistoryRedisTestAccessor accessor,
        out HistoryDatabaseService history)
    {
        var options = Options.Create(new HistoryDbOptions
        {
            DbDir = historyDbDir,
            RememberDays = 2,
            QueueCapacity = 1024,
            KeyPrefix = RedisKeyPrefix,
        });
        var metrics = new HistoryMetrics();
        var rocks = new RocksHistoryStore(options, metrics, NullLogger<RocksHistoryStore>.Instance);
        var tombstones = new HistoryReleaseTombstoneSet();
        var persistPump = new HistoryRocksPersistPump(
            rocks,
            tombstones,
            metrics,
            options,
            NullLogger<HistoryRocksPersistPump>.Instance);
        var memory = new Vector.NNTP.HistoryDB.Memory.HistoryMemoryCache(1_073_741_824, shardCount: 64, metrics);
        var redis = new HistoryRedisStore(
            options,
            accessor,
            metrics,
            NullLogger<HistoryRedisStore>.Instance);
        var lifetime = new TestHostApplicationLifetime();
        history = new HistoryDatabaseService(
            options,
            memory,
            redis,
            metrics,
            persistPump,
            rocks,
            tombstones,
            lifetime,
            NullLogger<HistoryDatabaseService>.Instance);
        history.SetOperational();
        return rocks;
    }

    /// <summary>
    /// Enqueues one item, completes the queue, and drains the pump worker loop.
    /// </summary>
    /// <param name="pump">Writer pump under test.</param>
    /// <param name="queue">Shared queue instance.</param>
    /// <param name="item">Queue item to process.</param>
    /// <returns>A task that completes when the pump exits.</returns>
    private static async Task RunPumpOnceAsync(
        NntpSpoolWriterPump pump,
        NntpSpoolWriteQueue queue,
        NntpSpoolWriteItem item)
    {
        Assert.That(queue.TryEnqueue(item), Is.True);
        queue.Complete();
        await pump.RunAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a queue item for pump tests.
    /// </summary>
    /// <param name="messageId">Transit command Message-ID.</param>
    /// <param name="article">Article bytes.</param>
    /// <returns>Queue item with sample origin metadata.</returns>
    private static NntpSpoolWriteItem CreateItem(string messageId, byte[] article)
    {
        return new NntpSpoolWriteItem(
            messageId,
            article,
            HistoryKeyEncoder.EncodeHexLower(messageId),
            SpoolTestOrigins.SpoolOrigin());
    }

    /// <summary>
    /// Builds a minimal valid transit article for pump tests.
    /// </summary>
    /// <param name="messageId">Message-ID header value.</param>
    /// <returns>Raw article bytes.</returns>
    private static byte[] BuildValidArticle(string messageId)
    {
        var builder = new StringBuilder();
        builder.Append("Path: misc.test\r\n");
        builder.Append("Message-ID: ");
        builder.Append(messageId);
        builder.Append("\r\nDate: Fri, 05 Jun 2026 12:00:00 +0000\r\n\r\n");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Waits until the persist pump writes a digest row or times out.
    /// </summary>
    /// <param name="rocks">Rocks store.</param>
    /// <param name="digest">Expected digest bytes.</param>
    /// <returns>A task that completes when the digest row appears or the wait elapses.</returns>
    private static async Task WaitForRocksPersistAsync(RocksHistoryStore rocks, byte[] digest)
    {
        for (int i = 0; i < 50 && rocks.GetDigestExpiration(digest) is null; i++)
        {
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tracks HistoryDB record and release calls for pump lifecycle tests.
    /// </summary>
    private sealed class TrackingHistoryDatabase : IHistoryDatabase
    {
        private readonly FakeHistoryDatabase _inner = new();

        /// <summary>
        /// Gets the number of <see cref="TryRecordAsync"/> calls.
        /// </summary>
        internal int RecordCount { get; private set; }

        /// <summary>
        /// Gets the number of <see cref="TryReleaseAsync"/> calls.
        /// </summary>
        internal int ReleaseCount { get; private set; }

        /// <summary>
        /// Gets the last message-id passed to <see cref="TryRecordAsync"/>.
        /// </summary>
        internal string? LastRecordedMessageId { get; private set; }

        /// <summary>
        /// Gets the last message-id passed to <see cref="TryReleaseAsync"/>.
        /// </summary>
        internal string? LastReleasedMessageId { get; private set; }

        /// <inheritdoc />
        public ValueTask<HistoryCheckResult> CheckAsync(string messageId, CancellationToken cancellationToken) =>
            this._inner.CheckAsync(messageId, cancellationToken);

        /// <inheritdoc />
        public async ValueTask<HistoryRecordResult> TryRecordAsync(string messageId, CancellationToken cancellationToken)
        {
            this.RecordCount++;
            this.LastRecordedMessageId = messageId;
            return await this._inner.TryRecordAsync(messageId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask<HistoryReleaseResult> TryReleaseAsync(string messageId, CancellationToken cancellationToken)
        {
            this.ReleaseCount++;
            this.LastReleasedMessageId = messageId;
            return await this._inner.TryReleaseAsync(messageId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Marks a message-id as already recorded.
        /// </summary>
        /// <param name="messageId">Message-id.</param>
        internal void SeedDuplicate(string messageId) => this._inner.SeedDuplicate(messageId);
    }

    /// <summary>
    /// In-memory spamd fake for history retention tests.
    /// </summary>
    private sealed class FailOpenSpamAssassin : ISpamAssassin
    {
        /// <summary>
        /// Gets or sets a value indicating whether <see cref="CheckAsync"/> should report spam.
        /// </summary>
        internal bool IsSpam { get; set; }

        /// <inheritdoc />
        public Task<SpamdCheckResult> CheckAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            _ = articleUtf8;
            _ = cancellationToken;
            SpamdCheckResult result = new(
                this.IsSpam,
                score: 6.0,
                threshold: 5.0,
                symbols: [],
                reportText: null,
                rawResponseHeaders: new Dictionary<string, string>());
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Minimal host lifetime for history service tests.
    /// </summary>
    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        /// <inheritdoc />
        public CancellationToken ApplicationStarted { get; } = CancellationToken.None;

        /// <inheritdoc />
        public CancellationToken ApplicationStopping { get; } = CancellationToken.None;

        /// <inheritdoc />
        public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

        /// <inheritdoc />
        public void StopApplication()
        {
        }
    }
}
