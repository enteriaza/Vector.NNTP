// <copyright file="NntpTransitPeerHostnameResolverTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Policy;
using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Tests.Sockets;

/// <summary>
/// Tests for <see cref="NntpTransitPeerHostnameResolver"/>.
/// </summary>
[TestFixture]
public sealed class NntpTransitPeerHostnameResolverTests
{
    /// <summary>
    /// Verifies AcceptFrom hostname entries are accepted as peer hostnames.
    /// </summary>
    [Test]
    public void IsAcceptableHostnameEntry_HostnameEntry_ReturnsTrue()
    {
        Assert.That(
            NntpTransitPeerHostnameResolver.IsAcceptableHostnameEntry("border-3.ord.giganews.com"),
            Is.True);
    }

    /// <summary>
    /// Verifies literal IP AcceptFrom entries are not treated as hostnames.
    /// </summary>
    [Test]
    public void IsAcceptableHostnameEntry_IpEntry_ReturnsFalse()
    {
        Assert.That(
            NntpTransitPeerHostnameResolver.IsAcceptableHostnameEntry("203.0.113.10"),
            Is.False);
    }

    /// <summary>
    /// Verifies CIDR AcceptFrom entries are not treated as hostnames.
    /// </summary>
    [Test]
    public void IsAcceptableHostnameEntry_CidrEntry_ReturnsFalse()
    {
        Assert.That(
            NntpTransitPeerHostnameResolver.IsAcceptableHostnameEntry("203.0.113.0/24"),
            Is.False);
    }

    /// <summary>
    /// Verifies matched hostname entries resolve without reverse DNS.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ResolveAsync_MatchedHostnameEntry_ReturnsEntry()
    {
        var connection = new NntpConnectionContext(
            "session-1",
            new IPEndPoint(IPAddress.Parse("203.0.113.10"), 119),
            new IPEndPoint(IPAddress.Loopback, 5000),
            NntpHostRole.Transit,
            "nntpd01",
            transitPeerName: "giganews",
            transitPeerMatchedEntry: "border-3.ord.giganews.com");

        string? host = await NntpTransitPeerHostnameResolver
            .ResolveAsync(connection, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(host, Is.EqualTo("border-3.ord.giganews.com"));
    }
}
