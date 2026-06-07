// <copyright file="SpamdScanArticleBuilderTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="SpamdScanArticleBuilder"/>.
/// </summary>
[TestFixture]
public sealed class SpamdScanArticleBuilderTests
{
    /// <summary>
    /// Verifies FQDN peer metadata produces a full Received clause and synthetic To address.
    /// </summary>
    [Test]
    public void BuildScanArticle_WithPeerHostName_AddsReceivedAndTo()
    {
        byte[] original = BuildSampleArticle();
        var builder = new SpamdScanArticleBuilder();
        var serverOptions = new NntpServerOptions
        {
            NodeName = "transit1",
            DomainName = "usenetninja.net",
        };

        byte[] scan = builder.BuildScanArticle(original, SpoolTestOrigins.SpoolOrigin(), serverOptions);
        string text = Encoding.UTF8.GetString(scan);

        Assert.That(text, Does.Contain("Received: from border-3.ord.giganews.com (border-3.ord.giganews.com [203.0.113.10])"));
        Assert.That(text, Does.Contain("by transit1.usenetninja.net"));
        Assert.That(text, Does.Contain("with NNTP;"));
        Assert.That(text, Does.Contain("Sun, 07 Jun 2026 18:42:17 +0000"));
        Assert.That(text, Does.Contain("To: usenet@transit1.usenetninja.net"));
        Assert.That(text, Does.Contain("X-Usenet-Newsgroups: misc.test"));
        Assert.That(text, Does.Not.Contain("Path:"));
        Assert.That(text, Does.Not.Contain("Xref:"));
    }

    /// <summary>
    /// Verifies IP-only Received form when peer hostname is unknown.
    /// </summary>
    [Test]
    public void BuildScanArticle_WithoutPeerHostName_UsesIpOnlyReceived()
    {
        byte[] original = BuildSampleArticle();
        var builder = new SpamdScanArticleBuilder();
        var origin = new Vector.NNTP.Articles.Storage.NntpSpoolArticleOrigin(
            IPAddress.Parse("203.0.113.10"),
            null,
            SpoolTestOrigins.SampleReceivedUtc);
        var serverOptions = new NntpServerOptions { NodeName = "transit1", DomainName = "usenetninja.net" };

        byte[] scan = builder.BuildScanArticle(original, origin, serverOptions);
        string text = Encoding.UTF8.GetString(scan);

        Assert.That(text, Does.Contain("Received: from [203.0.113.10]"));
        Assert.That(text, Does.Not.Contain("border-3.ord.giganews.com"));
    }

    /// <summary>
    /// Verifies the scan copy body bytes match the original article body.
    /// </summary>
    [Test]
    public void BuildScanArticle_PreservesBodyBytes()
    {
        byte[] original = BuildSampleArticle();
        var builder = new SpamdScanArticleBuilder();
        var serverOptions = new NntpServerOptions { NodeName = "node1" };

        byte[] scan = builder.BuildScanArticle(original, SpoolTestOrigins.SpoolOrigin(), serverOptions);

        int originalBodyStart = FindBodyStart(original);
        int scanBodyStart = FindBodyStart(scan);
        Assert.That(
            scan.AsSpan(scanBodyStart).ToArray(),
            Is.EqualTo(original.AsSpan(originalBodyStart).ToArray()));
    }

    /// <summary>
    /// Builds a sample article with operational headers that should be stripped from the scan copy.
    /// </summary>
    /// <returns>Original article bytes.</returns>
    private static byte[] BuildSampleArticle()
    {
        return Encoding.ASCII.GetBytes(
            "Path: peer!news\r\n" +
            "Message-ID: <scan@example.com>\r\n" +
            "From: poster@example.com\r\n" +
            "Subject: test\r\n" +
            "Date: Mon, 05 Jun 2026 12:00:00 +0000\r\n" +
            "Newsgroups: misc.test\r\n" +
            "Xref: news.example misc.test:1\r\n" +
            "\r\n" +
            "body line\r\n");
    }

    /// <summary>
    /// Finds the first body byte offset after the header terminator.
    /// </summary>
    /// <param name="articleBytes">Article bytes.</param>
    /// <returns>Body start index.</returns>
    private static int FindBodyStart(ReadOnlySpan<byte> articleBytes)
    {
        for (int i = 0; i < articleBytes.Length - 3; i++)
        {
            if (articleBytes[i] == (byte)'\r' && articleBytes[i + 1] == (byte)'\n' &&
                articleBytes[i + 2] == (byte)'\r' && articleBytes[i + 3] == (byte)'\n')
            {
                return i + 4;
            }
        }

        throw new InvalidOperationException("Header terminator not found.");
    }
}
