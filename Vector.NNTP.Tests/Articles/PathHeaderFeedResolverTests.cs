// <copyright file="PathHeaderFeedResolverTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Vector.NNTP.Articles.Logging;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="PathHeaderFeedResolver"/>.
/// </summary>
[TestFixture]
public sealed class PathHeaderFeedResolverTests
{
    /// <summary>
    /// Verifies the first hop before <c>!</c> is returned.
    /// </summary>
    [Test]
    public void TryResolveFeed_PathWithBang_ReturnsFirstHop()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: news.example.com!not-for-mail\r\nMessage-ID: <a@b>\r\n\r\n");

        Assert.That(
            PathHeaderFeedResolver.TryResolveFeed(article, out string feed),
            Is.True);
        Assert.That(feed, Is.EqualTo("news.example.com"));
    }

    /// <summary>
    /// Verifies bare <c>not-for-mail</c> Path values are ignored.
    /// </summary>
    [Test]
    public void TryResolveFeed_NotForMailOnly_ReturnsFalse()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Path: not-for-mail\r\nMessage-ID: <a@b>\r\n\r\n");

        Assert.That(PathHeaderFeedResolver.TryResolveFeed(article, out _), Is.False);
    }

    /// <summary>
    /// Verifies <see cref="PathHeaderFeedResolver.TryExtractFirstHop"/> rejects not-for-mail tokens.
    /// </summary>
    [Test]
    public void TryExtractFirstHop_NotForMail_ReturnsFalse()
    {
        Assert.That(
            PathHeaderFeedResolver.TryExtractFirstHop("not-for-mail"u8, out _),
            Is.False);
    }

    /// <summary>
    /// Verifies a hop after not-for-mail is not returned when it appears only after bang on same line.
    /// </summary>
    [Test]
    public void TryExtractFirstHop_NotForMailBeforeBang_ReturnsFalse()
    {
        Assert.That(
            PathHeaderFeedResolver.TryExtractFirstHop("not-for-mail!real.host"u8, out _),
            Is.False);
    }
}
