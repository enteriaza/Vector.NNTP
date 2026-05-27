// <copyright file="NntpReaderCommandGoldenTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC 3977 / 2980 reader command golden transcript tests.

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Golden transcript tests for reader commands after authentication.
    /// </summary>
    [TestFixture]
    public sealed class NntpReaderCommandGoldenTests
    {
        /// <summary>
        /// Verifies ARTICLE without GROUP returns 412.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Article_WithoutGroup_Returns412()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                await harness.AuthenticateAsync().ConfigureAwait(false);
                await harness.SendAsync("ARTICLE 1").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("412 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies ARTICLE after GROUP returns 220 and dot-terminated body.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Article_AfterGroup_Returns220AndBody()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                await harness.AuthenticateAsync().ConfigureAwait(false);
                await harness.SendAsync("GROUP test.local").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("ARTICLE 1").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("220 "));
                string bodyLine;
                do
                {
                    bodyLine = await harness.ReadLineAsync().ConfigureAwait(false);
                }
                while (bodyLine != ".");
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies OVER returns 224 multiline overview data.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Over_AfterGroup_Returns224()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                await harness.AuthenticateAsync().ConfigureAwait(false);
                await harness.SendAsync("GROUP test.local").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("OVER 1-2").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("224 "));
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("."));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies HDR returns 225 multiline header data.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Hdr_AfterGroup_Returns225()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                await harness.AuthenticateAsync().ConfigureAwait(false);
                await harness.SendAsync("GROUP test.local").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("HDR Subject 1-2").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("225 "));
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.Contain("header-value-1"));
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.Contain("header-value-2"));
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("."));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies LISTGROUP returns 215 article numbers.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ListGroup_AfterGroup_Returns215()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                await harness.AuthenticateAsync().ConfigureAwait(false);
                await harness.SendAsync("GROUP test.local").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("LISTGROUP").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("215 "));
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("1"));
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("2"));
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Is.EqualTo("."));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies NEXT advances the article pointer with 223.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Next_AfterGroup_Returns223()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                await harness.AuthenticateAsync().ConfigureAwait(false);
                await harness.SendAsync("GROUP test.local").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("STAT 1").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("NEXT").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("223 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies NEWGROUPS and SLAVE return 503.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ExcludedCommands_Return503()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                await harness.AuthenticateAsync().ConfigureAwait(false);
                await harness.SendAsync("NEWGROUPS 0000000000 000000 GMT").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("503 "));
                await harness.SendAsync("SLAVE").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("503 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
