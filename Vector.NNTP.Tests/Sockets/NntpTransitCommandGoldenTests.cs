// <copyright file="NntpTransitCommandGoldenTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC 4644 transit command golden transcript tests.

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
                Assert.That(line, Does.StartWith("238 "));
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
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                await NntpProtocolHarness.AuthenticateTransitAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("IHAVE <stored@test.local>").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("335 "));
                await harness.SendArticleBodyAsync("Subject: stored\r\nMessage-ID: <stored@test.local>\r\n\r\nbody\r\n").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("235 "));
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("CHECK <stored@test.local>").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("438 "));
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
    }
}
