// <copyright file="NntpSpoolWriterPumpNewsLogTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Diagnostics;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Filters.PostFilter;
using Vector.NNTP.Filters.SpamAssassin;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Tests.HistoryDB.Fakes;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests that <see cref="NntpSpoolWriterPump"/> emits INN news log lines at the correct pipeline boundaries.
/// </summary>
[TestFixture]
public sealed class NntpSpoolWriterPumpNewsLogTests
{
    /// <summary>
    /// Temporary spool root created for write-success tests.
    /// </summary>
    private string? _spoolDirectory;

    /// <summary>
    /// Creates a unique spool directory before each test that performs disk writes.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        this._spoolDirectory = Path.Combine(Path.GetTempPath(), "vector-nntp-pump-newslog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._spoolDirectory);
    }

    /// <summary>
    /// Deletes the temporary spool directory after each test.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (this._spoolDirectory is not null && Directory.Exists(this._spoolDirectory))
        {
            Directory.Delete(this._spoolDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies preprocess failures emit a reject line and never an accept line.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task RunAsync_PreprocessFailure_LogsRejectWithReason()
    {
        var newsLog = new RecordingNntpNewsLog();
        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) = CreatePumpWithQueue(newsLog);
        byte[] article =
            "Path: misc.test\r\nMessage-ID: <a@b>\r\nSubject:missing-space\r\n\r\n"u8.ToArray();

        await RunPumpOnceAsync(
            pump,
            queue,
            CreateItem("<a@b>", article)).ConfigureAwait(false);

        Assert.That(newsLog.LastStatus, Is.EqualTo('-'));
        Assert.That(newsLog.LastReason, Does.Contain("line 3"));
        Assert.That(newsLog.Entries.Any(entry => entry.Status == '+'), Is.False);
    }

    /// <summary>
    /// Verifies SpamAssassin spam classification emits a reject line, not junk or accept lines.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task RunAsync_SpamPostprocessFailure_LogsRejectNotJunk()
    {
        var newsLog = new RecordingNntpNewsLog();
        var spamAssassin = new FakeSpamAssassin { IsSpam = true };
        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) = CreatePumpWithQueue(newsLog, spamAssassin);
        byte[] article = BuildValidArticle("<spam@example.com>");

        await RunPumpOnceAsync(
            pump,
            queue,
            CreateItem("<spam@example.com>", article)).ConfigureAwait(false);

        Assert.That(newsLog.LastStatus, Is.EqualTo('-'));
        Assert.That(newsLog.LastReason, Does.Contain("SpamAssassin"));
        Assert.That(newsLog.Entries.Any(entry => entry.Status == 'j'), Is.False);
        Assert.That(newsLog.Entries.Any(entry => entry.Status == '+'), Is.False);
    }

    /// <summary>
    /// Verifies yEnc CRC failures emit a reject line with the yEnc reason.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task RunAsync_YEncCrcFailure_LogsRejectWithReason()
    {
        var newsLog = new RecordingNntpNewsLog();
        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) = CreatePumpWithQueue(newsLog);
        byte[] article = BuildInvalidYEncArticle("<yenc@example.com>");

        await RunPumpOnceAsync(
            pump,
            queue,
            CreateItem("<yenc@example.com>", article)).ConfigureAwait(false);

        Assert.That(newsLog.LastStatus, Is.EqualTo('-'));
        Assert.That(newsLog.LastReason, Is.EqualTo("yEnc section CRC validation failed."));
        Assert.That(newsLog.Entries.Any(entry => entry.Status == '+'), Is.False);
    }

    /// <summary>
    /// Verifies accept lines are emitted only after a successful atomic spool write.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task RunAsync_WriteSuccess_LogsAcceptAfterCommit()
    {
        const string messageId = "<committed@example.com>";
        var newsLog = new RecordingNntpNewsLog();
        (NntpSpoolWriterPump pump, NntpSpoolWriteQueue queue) =
            CreatePumpWithQueue(newsLog, spoolDirectory: this._spoolDirectory);
        byte[] article = BuildValidArticle(messageId);

        await RunPumpOnceAsync(
            pump,
            queue,
            CreateItem(messageId, article)).ConfigureAwait(false);

        string digestHex = HistoryKeyEncoder.EncodeHexLower(messageId);
        string articlePath = SpoolDirectoryUtilities.GetArticleFilePath(this._spoolDirectory!, digestHex);

        Assert.That(File.Exists(articlePath), Is.True);
        Assert.That(newsLog.Entries, Has.Count.EqualTo(1));
        Assert.That(newsLog.LastStatus, Is.EqualTo('+'));
        Assert.That(newsLog.LastMessageId, Is.EqualTo(messageId));
    }

    /// <summary>
    /// Builds a pump and queue pair sharing one queue instance for test orchestration.
    /// </summary>
    /// <param name="newsLog">Recording news log test double.</param>
    /// <param name="spamAssassin">Optional spamd client override.</param>
    /// <param name="spoolDirectory">Optional spool root override.</param>
    /// <returns>Pump and queue tuple for enqueue and drain.</returns>
    private static (NntpSpoolWriterPump Pump, NntpSpoolWriteQueue Queue) CreatePumpWithQueue(
        RecordingNntpNewsLog newsLog,
        ISpamAssassin? spamAssassin = null,
        string? spoolDirectory = null)
    {
        var queueOptions = Options.Create(new NntpServerOptions
        {
            SpoolQueueCapacity = 4,
            MaxQueuedBytes = 4_194_304,
            SpoolDir = spoolDirectory ?? string.Empty,
        });
        var serverOptions = Options.Create(new NntpServerOptions
        {
            NodeName = "transit1",
            DomainName = "usenetninja.net",
            MaxArtSize = 4_194_304,
            SpoolDir = spoolDirectory ?? string.Empty,
        });
        var queue = new NntpSpoolWriteQueue(queueOptions, new NntpSpoolMetrics());
        var preprocessor = new ArticleSpoolPreprocessor(serverOptions);
        var postprocessor = new ArticleSpoolPostprocessor(
            Options.Create(new PostFilterOptions()),
            serverOptions,
            spamAssassin ?? new FakeSpamAssassin(),
            new SpamdScanArticleBuilder(),
            NullLogger<ArticleSpoolPostprocessor>.Instance);
        var pump = new NntpSpoolWriterPump(
            queue,
            preprocessor,
            postprocessor,
            new FakeHistoryDatabase(),
            new NntpSpoolMetrics(),
            serverOptions,
            newsLog,
            NullLogger<NntpSpoolWriterPump>.Instance);

        return (pump, queue);
    }

    /// <summary>
    /// Enqueues one item on the supplied queue, completes it, and drains the pump worker loop.
    /// </summary>
    /// <param name="pump">Writer pump under test.</param>
    /// <param name="queue">Shared queue instance wired into the pump.</param>
    /// <param name="item">Queue item to process.</param>
    /// <returns>A task that completes when the pump exits after queue completion.</returns>
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
    /// Builds a yEnc article whose section CRC validation fails during postprocessing.
    /// </summary>
    /// <param name="messageId">Message-ID header value.</param>
    /// <returns>Raw article bytes.</returns>
    private static byte[] BuildInvalidYEncArticle(string messageId)
    {
        var builder = new StringBuilder();
        builder.Append("Path: misc.test\r\n");
        builder.Append("Message-ID: ");
        builder.Append(messageId);
        builder.Append("\r\nDate: Fri, 05 Jun 2026 12:00:00 +0000\r\n\r\n");
        builder.Append("=ybegin line=128 size=10 name=test.dat\r\n");
        builder.Append("incomplete section without yend\r\n");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>
    /// In-memory <see cref="ISpamAssassin"/> fake for pump news log tests.
    /// </summary>
    private sealed class FakeSpamAssassin : ISpamAssassin
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
}
