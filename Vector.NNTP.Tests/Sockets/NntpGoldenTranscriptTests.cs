// <copyright file="NntpGoldenTranscriptTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: RFC-aligned golden transcript protocol tests over in-memory pipes.

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Golden transcript tests for core NNTP responses (RFC 3977 / 4643 gating).
    /// </summary>
    [TestFixture]
    public sealed class NntpGoldenTranscriptTests
    {
        /// <summary>
        /// Verifies the reader server sends a 201 no-posting greeting before authentication.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Greeting_ReaderUnauthenticated_Is201NoPosting()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                string greeting = await harness.ReadGreetingAsync().ConfigureAwait(false);
                Assert.That(greeting, Does.StartWith("201 "));
                Assert.That(greeting, Does.Contain("VectorNNTPD-Test"));
                Assert.That(greeting, Does.Contain("posting not allowed"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies an unrecognised verb returns 500, not 480, before authentication.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task UnknownCommand_WhenUnauthenticated_Returns500()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("e").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Is.EqualTo("500 Unknown command"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies unauthenticated GROUP returns 480 per security ordering.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Group_WhenUnauthenticated_Returns480()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("GROUP test.local").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("480 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies CAPABILITIES lists VERSION before authentication.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Capabilities_BeforeAuth_ListsVersion()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("CAPABILITIES").ConfigureAwait(false);
                string first = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(first, Does.StartWith("101 "));
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Is.EqualTo("VERSION 2"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies AUTHINFO USER/PASS authenticates and GROUP succeeds.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task AuthInfoUserPass_ThenGroup_Succeeds()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("AUTHINFO USER alice").ConfigureAwait(false);
                string passPrompt = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(passPrompt, Does.StartWith("381 "));
                await harness.SendAsync("AUTHINFO PASS secret").ConfigureAwait(false);
                string ok = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(ok, Does.StartWith("281 "));
                await harness.SendAsync("GROUP test.local").ConfigureAwait(false);
                string group = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(group, Does.StartWith("211 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies MODE READER re-sends the no-posting greeting when unauthenticated.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ModeReader_OnReaderProfile_Returns201WhenUnauthenticated()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("MODE READER").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("201 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies the transit server sends the legacy NNTPD greeting on connect.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Greeting_Transit_Is200PostingAllowed()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                string greeting = await harness.ReadGreetingAsync().ConfigureAwait(false);
                Assert.That(greeting, Is.EqualTo("200 VectorNNTPD-Test ready - posting allowed"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies transit CAPABILITIES advertises authentication and lists STREAM once.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Capabilities_Transit_AdvertisesAuthAndSingleStream()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("CAPABILITIES").ConfigureAwait(false);
                List<string> lines = await harness.ReadMultiLineAsync().ConfigureAwait(false);
                Assert.That(lines[0], Does.StartWith("101 "));
                Assert.That(lines, Does.Contain("VERSION 2"));
                Assert.That(lines, Does.Contain("STREAM"));
                Assert.That(lines.Count(l => l == "STREAM"), Is.EqualTo(1));
                Assert.That(lines.Any(l => l.StartsWith("AUTHINFO USER", StringComparison.Ordinal)), Is.True);
                Assert.That(lines.Any(l => l.StartsWith("SASL", StringComparison.Ordinal)), Is.True);
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies reader CAPABILITIES includes SCRAM-SHA-256 when a SCRAM store is present.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Capabilities_Reader_WithScramStore_AdvertisesScramSha256()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReaderWithScram(new Fakes.FakeScramCredentialStore());
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("CAPABILITIES").ConfigureAwait(false);
                List<string> lines = await harness.ReadMultiLineAsync().ConfigureAwait(false);
                string? sasl = lines.FirstOrDefault(l => l.StartsWith("SASL ", StringComparison.Ordinal));
                Assert.That(sasl, Is.Not.Null);
                Assert.That(sasl, Does.Contain("SCRAM-SHA-256"));
                Assert.That(sasl, Does.Not.Contain("SCRAM-SHA-1"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies transit CAPABILITIES includes SCRAM-SHA-256 when a SCRAM store is present.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Capabilities_Transit_WithScramStore_AdvertisesScramSha256()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransitWithScram(new Fakes.FakeScramCredentialStore());
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("CAPABILITIES").ConfigureAwait(false);
                List<string> lines = await harness.ReadMultiLineAsync().ConfigureAwait(false);
                string? sasl = lines.FirstOrDefault(l => l.StartsWith("SASL ", StringComparison.Ordinal));
                Assert.That(sasl, Is.Not.Null);
                Assert.That(sasl, Does.Contain("SCRAM-SHA-256"));
                Assert.That(sasl, Does.Not.Contain("SCRAM-SHA-1"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies MODE STREAM on transit profile returns 203 per RFC 4644.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ModeStream_OnTransitProfile_Returns203()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("203 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies QUIT returns 205 and closes the transport (EOF).
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Quit_Returns205AndClosesTransport()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("QUIT").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("205 "));
                string eof = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(eof, Is.Empty);
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies NEWNEWS returns 503 (not supported).
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Newnews_WhenAuthenticated_Returns503()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("AUTHINFO USER alice").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("AUTHINFO PASS secret").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("NEWNEWS * 0000000000 000000 GMT").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("503 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
