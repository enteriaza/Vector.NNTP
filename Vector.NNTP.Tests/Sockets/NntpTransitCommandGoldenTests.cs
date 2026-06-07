// <copyright file="NntpTransitCommandGoldenTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC 4644 transit command golden transcript tests.

using Vector.NNTP.Sockets.Storage;
using Vector.NNTP.Tests.HistoryDB.Fakes;
using Vector.NNTP.Tests.Sockets.Fakes;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Golden transcript tests for MODE STREAM transit commands.
    /// </summary>
    [TestFixture]
    public sealed class NntpTransitCommandGoldenTests
    {
        /// <summary>
        /// Verifies CHECK returns 238 when the article is wanted.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Check_UnknownMessageId_Returns238()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("CHECK <new@test.local>").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Is.EqualTo("238 <new@test.local>"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies CHECK returns 438 when the article is already stored.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Check_KnownMessageId_Returns438()
        {
            const string messageId = "<stored@test.local>";
            var history = new FakeHistoryDatabase();
            history.SeedDuplicate(messageId);
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit(history);
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("CHECK " + messageId).ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Is.EqualTo("438 " + messageId));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies CHECK is read-only: repeated CHECK returns 238 until TAKETHIS records the id.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Check_BeforeTakethis_DoesNotReserve_SecondCheckStill238()
        {
            const string messageId = "<probe@test.local>";
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("CHECK " + messageId).ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("238 " + messageId));
                await harness.SendAsync("CHECK " + messageId).ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("238 " + messageId));
                await harness.SendTakethisWithArticleAsync(
                    messageId,
                    "Subject: probe\r\nMessage-ID: " + messageId + "\r\n\r\nbody\r\n").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("239 "));
                await harness.SendAsync("CHECK " + messageId).ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("438 " + messageId));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies IHAVE accepts an article and returns 235.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task IHave_TransfersArticle_Returns235()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("IHAVE <ihave@test.local>").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("335 "));
                await harness.SendArticleBodyAsync("Subject: ihave\r\nMessage-ID: <ihave@test.local>\r\n\r\nbody\r\n").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("235 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies IHAVE maps <see cref="NntpTransitStorageResult.ArticleRejected"/> to <c>437</c>, not <c>436</c>.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task IHave_ArticleRejected_Returns437()
        {
            var transitStorage = new FakeNntpTransitStorage
            {
                TakeThisResult = NntpTransitStorageResult.ArticleRejected,
            };
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit(new FakeHistoryDatabase(), transitStorage);
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("IHAVE <reject@test.local>").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("335 "));
                await harness.SendArticleBodyAsync("Subject: reject\r\nMessage-ID: <reject@test.local>\r\n\r\nbody\r\n")
                    .ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("437 Article rejected"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies TAKETHIS maps <see cref="NntpTransitStorageResult.ArticleRejected"/> to <c>439</c>, not <c>431</c>.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Takethis_ArticleRejected_Returns439()
        {
            const string messageId = "<reject-takethis@test.local>";
            var transitStorage = new FakeNntpTransitStorage
            {
                TakeThisResult = NntpTransitStorageResult.ArticleRejected,
            };
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit(new FakeHistoryDatabase(), transitStorage);
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendTakethisWithArticleAsync(
                    messageId,
                    "Subject: reject\r\nMessage-ID: " + messageId + "\r\n\r\nbody\r\n").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("439 Transfer failed"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies TAKETHIS with a multi-line article (including dot-stuffing) returns 239.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Takethis_MultiLineArticle_Returns239()
        {
            const string messageId = "<multiline@test.local>";
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendTakethisWithArticleAsync(
                    messageId,
                    "Path: example\r\nFrom: a@b\r\n..leading-dot\r\nMessage-ID: " + messageId + "\r\n\r\nbody\r\n")
                    .ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("239 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies transit commands reject syntactically invalid Message-IDs with 501.
        /// </summary>
        /// <param name="commandLine">Full command line including verb.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [TestCase("CHECK <a@>")]
        [TestCase("IHAVE <a@>")]
        public async Task Transit_InvalidMessageId_Returns501Invalid(string commandLine)
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                if (commandLine.StartsWith("CHECK", StringComparison.Ordinal))
                {
                    await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                    _ = await harness.ReadLineAsync().ConfigureAwait(false);
                }

                await harness.SendAsync(commandLine).ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Is.EqualTo("501 Invalid Message-ID"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies pipelined TAKETHIS rejects invalid Message-IDs after draining the article body.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Takethis_InvalidMessageId_WithPipelinedBody_Returns501Invalid()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendTakethisWithArticleAsync(
                    "<a@>",
                    "Subject: bad\r\n\r\nbody\r\n").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Is.EqualTo("501 Invalid Message-ID"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies TAKETHIS after CHECK returns 239 with no intermediate 373 (RFC 4644 streaming).
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Takethis_AfterCheck_Returns239_No373()
        {
            const string messageId = "<takethis@test.local>";
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("CHECK " + messageId).ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("238 " + messageId));
                await harness.SendTakethisWithArticleAsync(
                    messageId,
                    "Subject: takethis\r\nMessage-ID: " + messageId + "\r\n\r\nbody\r\n").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("239 "));
                Assert.That(line, Does.Not.StartWith("373 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
