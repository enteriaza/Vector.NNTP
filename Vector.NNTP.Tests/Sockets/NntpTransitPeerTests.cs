// <copyright file="NntpTransitPeerTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.Session.Coordination;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Tests for trusted transit peer matching, validation, and admission.
    /// </summary>
    [TestFixture]
    public sealed class NntpTransitPeerTests
    {
        /// <summary>
        /// Verifies overlapping peer CIDR definitions fail startup validation.
        /// </summary>
        [Test]
        public void Validate_OverlappingCidr_Fails()
        {
            var options = new NntpServerOptions
            {
                NodeName = "test-node",
                ServerIdentification = "test",
                TransitPeers = new NntpTransitPeersOptions
                {
                    Peers =
                    [
                        new NntpTransitPeerOptions
                        {
                            PeerId = "peer-a",
                            Name = "A",
                            AcceptFrom = ["10.0.0.0/8"],
                        },
                        new NntpTransitPeerOptions
                        {
                            PeerId = "peer-b",
                            Name = "B",
                            AcceptFrom = ["10.1.0.0/16"],
                        },
                    ],
                },
            };

            ValidateOptionsResult result = new NntpServerOptionsValidator().Validate(null, options);
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("overlap").IgnoreCase);
        }

        /// <summary>
        /// Verifies in-memory ZSET admission enforces AcceptMaxConnections.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task InMemoryCoordinator_EnforcesMaxConnections()
        {
            var coordinator = new InMemoryTransitPeerCoordinator();
            for (int i = 0; i < 10; i++)
            {
                NntpTransitPeerAdmissionResult result = await coordinator.TryAcquireAsync(
                    "giganews",
                    Guid.NewGuid().ToString("N"),
                    maxConnections: 10,
                    leaseSeconds: 300,
                    "test-node",
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(result, Is.EqualTo(NntpTransitPeerAdmissionResult.Success));
            }

            NntpTransitPeerAdmissionResult denied = await coordinator.TryAcquireAsync(
                "giganews",
                Guid.NewGuid().ToString("N"),
                maxConnections: 10,
                leaseSeconds: 300,
                "test-node",
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(denied, Is.EqualTo(NntpTransitPeerAdmissionResult.AtCapacity));
        }

        /// <summary>
        /// Verifies CHECK works without AUTH when TransitPeerId is set on the harness context.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Check_WithoutAuth_WhenTrustedTransitPeer_Returns238()
        {
            NntpProtocolHarness harness = CreateTrustedTransitHarness();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("CHECK <peer@test.local>").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("238 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies TAKETHIS after CHECK returns 239 without AUTH when TransitPeerId is set.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Takethis_WithoutAuth_WhenTrustedTransitPeer_Returns239()
        {
            const string messageId = "<peer-takethis@test.local>";
            NntpProtocolHarness harness = CreateTrustedTransitHarness();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("MODE STREAM").ConfigureAwait(false);
                _ = await harness.ReadLineAsync().ConfigureAwait(false);
                await harness.SendAsync("CHECK " + messageId).ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("238 "));
                await harness.SendTakethisWithArticleAsync(
                    messageId,
                    "Subject: peer\r\nMessage-ID: " + messageId + "\r\n\r\nbody\r\n").ConfigureAwait(false);
                Assert.That(await harness.ReadLineAsync().ConfigureAwait(false), Does.StartWith("239 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies CHECK before AUTH returns 480 for a normal transit harness without peer identity.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task Check_WithoutAuth_WithoutPeer_Returns480()
        {
            NntpProtocolHarness harness = NntpProtocolHarness.CreateTransit();
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                await harness.SendAsync("CHECK <peer@test.local>").ConfigureAwait(false);
                string line = await harness.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line, Does.StartWith("480 "));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Creates a transit harness with trusted peer identity pre-set.</summary>
        /// <returns>Connected harness.</returns>
        private static NntpProtocolHarness CreateTrustedTransitHarness()
        {
            return NntpProtocolHarness.CreateTransitTrustedPeer("giganews", "Giganews Test");
        }
    }
}
