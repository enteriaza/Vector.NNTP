// <copyright file="NntpCpuOverloadHarnessTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Tests.Sockets.Fakes;

namespace Vector.NNTP.Tests.Sockets;

/// <summary>
/// Verifies CPU overload <c>400</c> rejection on established sessions.
/// </summary>
[TestFixture]
public sealed class NntpCpuOverloadHarnessTests
{
    /// <summary>
    /// Verifies mid-session command receives 400 and closes.
    /// </summary>
    /// <returns>A task that completes when assertions finish.</returns>
    [Test]
    public async Task CommandOverload_Returns400AndCloses()
    {
        var monitor = new FakeNntpCpuLoadMonitor();
        NntpProtocolHarness harness = NntpProtocolHarness.CreateReader(monitor);
        try
        {
            string greeting = await harness.ReadGreetingAsync().ConfigureAwait(false);
            Assert.That(greeting, Does.StartWith("20"));

            monitor.Overloaded = true;
            await harness.SendAsync("CAPABILITIES", CancellationToken.None).ConfigureAwait(false);
            string response = await harness.ReadLineAsync().ConfigureAwait(false);
            Assert.That(response, Is.EqualTo(NntpResponseLines.ServiceUnavailable400));

            string afterClose = await harness.ReadLineAsync().ConfigureAwait(false);
            Assert.That(afterClose, Is.Empty);
        }
        finally
        {
            await harness.DisposeAsync().ConfigureAwait(false);
        }
    }
}
