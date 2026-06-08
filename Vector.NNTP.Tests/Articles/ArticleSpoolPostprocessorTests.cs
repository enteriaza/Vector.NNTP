// <copyright file="ArticleSpoolPostprocessorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Filters.PostFilter;
using Vector.NNTP.Filters.SpamAssassin;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="ArticleSpoolPostprocessor"/>.
/// </summary>
[TestFixture]
public sealed class ArticleSpoolPostprocessorTests
{
    /// <summary>
    /// Verifies a well-formed article with matching Message-ID and parseable Date succeeds.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_ValidArticle_ReturnsSuccess()
    {
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor();
        byte[] article = BuildValidArticle("<valid@example.com>");
        NntpSpoolWriteItem item = CreateItem("<valid@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ArticleBytes, Is.SameAs(article));
    }

    /// <summary>
    /// Verifies a missing <c>Message-ID</c> header is rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_MissingMessageIdHeader_ReturnsFailure()
    {
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor();
        byte[] article = "Path: misc.test\r\nDate: Fri, 05 Jun 2026 12:00:00 +0000\r\n\r\n"u8.ToArray();
        NntpSpoolWriteItem item = CreateItem("<missing@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("Message-ID"));
    }

    /// <summary>
    /// Verifies a <c>Message-ID</c> header that does not match the transit command token is rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_MessageIdMismatch_ReturnsFailure()
    {
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor();
        byte[] article = BuildValidArticle("<header@example.com>");
        NntpSpoolWriteItem item = CreateItem("<command@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("does not match"));
    }

    /// <summary>
    /// Verifies an unparsable <c>Date</c> header is rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_InvalidDateHeader_ReturnsFailure()
    {
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor();
        byte[] article =
            "Path: misc.test\r\nMessage-ID: <date@example.com>\r\nDate: not-a-date\r\n\r\n"u8.ToArray();
        NntpSpoolWriteItem item = CreateItem("<date@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("Date"));
    }

    /// <summary>
    /// Verifies configured forbidden headers are rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_ForbiddenHeader_ReturnsFailure()
    {
        var options = new PostFilterOptions
        {
            Style = new PostFilterStyleOptions
            {
                ForbiddenHeaderNames = ["X-Bad"],
            },
        };
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor(postFilterOptions: options);
        byte[] article =
            "Path: misc.test\r\nMessage-ID: <bad@example.com>\r\nDate: Fri, 05 Jun 2026 12:00:00 +0000\r\nX-Bad: yes\r\n\r\n"u8.ToArray();
        NntpSpoolWriteItem item = CreateItem("<bad@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("Forbidden header"));
    }

    /// <summary>
    /// Verifies spam classification rejects small non-yEnc articles.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_SpamDetected_ReturnsFailure()
    {
        var spamAssassin = new FakeSpamAssassin { IsSpam = true };
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor(spamAssassin: spamAssassin);
        byte[] article = BuildValidArticle("<spam@example.com>");
        NntpSpoolWriteItem item = CreateItem("<spam@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("SpamAssassin"));
        Assert.That(spamAssassin.CheckCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies articles larger than the post-filter default (1 MiB) but within
    /// <see cref="NntpServerOptions.MaxArtSize"/> are accepted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_ArticleWithinMaxArtSize_ReturnsSuccess()
    {
        const int maxArtSize = 4_194_304;
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor(
            serverOptions: new NntpServerOptions
            {
                NodeName = "transit1",
                DomainName = "usenetninja.net",
                MaxArtSize = maxArtSize,
            });
        byte[] article = BuildValidArticle("<large@example.com>", bodyByteCount: 2_000_000);
        NntpSpoolWriteItem item = CreateItem("<large@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(article.Length, Is.LessThanOrEqualTo(maxArtSize));
        Assert.That(article.Length, Is.GreaterThan(1_048_576));
    }

    /// <summary>
    /// Verifies articles exceeding <see cref="NntpServerOptions.MaxArtSize"/> are rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_ExceedsMaxArtSize_ReturnsFailure()
    {
        const int maxArtSize = 1_048_576;
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor(
            serverOptions: new NntpServerOptions
            {
                NodeName = "transit1",
                DomainName = "usenetninja.net",
                MaxArtSize = maxArtSize,
            });
        byte[] article = BuildValidArticle("<oversize@example.com>", bodyByteCount: maxArtSize);
        NntpSpoolWriteItem item = CreateItem("<oversize@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("maximum size"));
        Assert.That(result.FailureReason, Does.Contain(maxArtSize.ToString()));
    }

    /// <summary>
    /// Verifies spamd errors fail open and preserve original article bytes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task PostprocessAsync_SpamdError_FailsOpen()
    {
        var spamAssassin = new FakeSpamAssassin { ThrowProtocolError = true };
        ArticleSpoolPostprocessor postprocessor = CreatePostprocessor(spamAssassin: spamAssassin);
        byte[] article = BuildValidArticle("<failopen@example.com>");
        NntpSpoolWriteItem item = CreateItem("<failopen@example.com>", article);

        ArticleSpoolPostprocessResult result = await postprocessor
            .PostprocessAsync(item, article, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ArticleBytes, Is.SameAs(article));
    }

    /// <summary>
    /// Builds a postprocessor with default dependencies.
    /// </summary>
    /// <param name="postFilterOptions">Optional post-filter options override.</param>
    /// <param name="serverOptions">Optional server options override.</param>
    /// <param name="spamAssassin">Optional spamd client override.</param>
    /// <returns>Configured postprocessor instance.</returns>
    private static ArticleSpoolPostprocessor CreatePostprocessor(
        PostFilterOptions? postFilterOptions = null,
        NntpServerOptions? serverOptions = null,
        ISpamAssassin? spamAssassin = null)
    {
        return new ArticleSpoolPostprocessor(
            Options.Create(postFilterOptions ?? new PostFilterOptions()),
            Options.Create(serverOptions ?? new NntpServerOptions
            {
                NodeName = "transit1",
                DomainName = "usenetninja.net",
                MaxArtSize = 4_194_304,
            }),
            spamAssassin ?? new FakeSpamAssassin(),
            new SpamdScanArticleBuilder(),
            new NntpSpoolMetrics(),
            NullLogger<ArticleSpoolPostprocessor>.Instance);
    }

    /// <summary>
    /// Builds a queue item for postprocessor tests.
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
    /// Builds a minimal valid transit article for postprocessing tests.
    /// </summary>
    /// <param name="messageId">Message-ID header value.</param>
    /// <param name="bodyByteCount">Optional body byte count after the header terminator.</param>
    /// <returns>Raw article bytes.</returns>
    private static byte[] BuildValidArticle(string messageId, int bodyByteCount = 0)
    {
        var builder = new StringBuilder();
        builder.Append("Path: misc.test\r\n");
        builder.Append("Message-ID: ");
        builder.Append(messageId);
        builder.Append("\r\nDate: Fri, 05 Jun 2026 12:00:00 +0000\r\n\r\n");
        if (bodyByteCount > 0)
        {
            builder.Append('x', bodyByteCount);
        }

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>
    /// In-memory <see cref="ISpamAssassin"/> fake for postprocessor tests.
    /// </summary>
    private sealed class FakeSpamAssassin : ISpamAssassin
    {
        /// <summary>
        /// Gets or sets a value indicating whether <see cref="CheckAsync"/> should report spam.
        /// </summary>
        internal bool IsSpam { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="CheckAsync"/> should throw <see cref="SpamdProtocolException"/>.
        /// </summary>
        internal bool ThrowProtocolError { get; set; }

        /// <summary>
        /// Gets the number of <see cref="CheckAsync"/> invocations.
        /// </summary>
        internal int CheckCount { get; private set; }

        /// <inheritdoc />
        public Task<SpamdCheckResult> CheckAsync(ReadOnlyMemory<byte> articleUtf8, CancellationToken cancellationToken = default)
        {
            _ = articleUtf8;
            _ = cancellationToken;
            this.CheckCount++;
            if (this.ThrowProtocolError)
            {
                throw new SpamdProtocolException("stub failure");
            }

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
