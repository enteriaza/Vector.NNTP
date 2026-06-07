// <copyright file="NntpNewsFeedResolverTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Net;
using System.Text;
using Vector.NNTP.Articles.Logging;
using Vector.NNTP.Articles.Storage;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="NntpNewsFeedResolver"/>.
/// </summary>
[TestFixture]
public sealed class NntpNewsFeedResolverTests
{
    /// <summary>
    /// Verifies local posts use the <c>local</c> feed token.
    /// </summary>
    [Test]
    public void ResolveFeed_LocalPost_ReturnsLocal()
    {
        var origin = new NntpSpoolArticleOrigin(
            IPAddress.Loopback,
            PeerHostName: null,
            ReceivedUtc: DateTimeOffset.UtcNow,
            TransitPeerName: null,
            IsLocalPost: true);

        Assert.That(
            NntpNewsFeedResolver.ResolveFeed(origin, ReadOnlySpan<byte>.Empty),
            Is.EqualTo(NntpNewsLogFeedNames.Local));
    }

    /// <summary>
    /// Verifies transit peer name wins over Path and hostname.
    /// </summary>
    [Test]
    public void ResolveFeed_TransitPeerName_ReturnsPeerName()
    {
        var origin = new NntpSpoolArticleOrigin(
            IPAddress.Parse("203.0.113.10"),
            "border.example.com",
            DateTimeOffset.UtcNow,
            TransitPeerName: "Giganews");
        byte[] article = Encoding.ASCII.GetBytes("Path: other.example.com\r\n\r\n");

        Assert.That(
            NntpNewsFeedResolver.ResolveFeed(origin, article),
            Is.EqualTo("Giganews"));
    }
}
