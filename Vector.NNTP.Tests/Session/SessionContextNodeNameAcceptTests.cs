// <copyright file="SessionContextNodeNameAcceptTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Tests.Sockets;
using Vector.NNTP.Tests.Sockets.Fakes;

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Verifies <see cref="SessionContext.NodeName"/> is stamped when a connection is accepted.
    /// </summary>
    [TestFixture]
    public sealed class SessionContextNodeNameAcceptTests
    {
        /// <summary>
        /// Verifies the node-local session row records <c>test-node</c> from server options.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task RunAsync_RegistersSessionContextWithNodeName()
        {
            NntpSessionTestServices.NntpSessionTestBundle bundle = NntpSessionTestServices.CreateDefault();
            var validator = new FakeNntpCredentialValidator(new Dictionary<string, string> { ["alice"] = "secret" });
            NntpProtocolHarness harness = NntpProtocolHarness.CreateReader(bundle, validator);
            try
            {
                _ = await harness.ReadGreetingAsync().ConfigureAwait(false);
                IReadOnlyCollection<SessionContext> rows = bundle.Database.SnapshotAll();
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(rows.First().NodeName, Is.EqualTo("test-node"));
            }
            finally
            {
                await harness.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
