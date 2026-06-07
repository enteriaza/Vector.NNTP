// <copyright file="ArticleByteScanSimdTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Vector.NNTP.Articles.Scanning;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="ArticleByteScanSimd"/> separator and newline scanning.
/// </summary>
[TestFixture]
public sealed class ArticleByteScanSimdTests
{
    /// <summary>
    /// Verifies <see cref="ArticleByteScanSimd.FindHeaderEnd"/> locates <c>\r\n\r\n</c> separators.
    /// </summary>
    [Test]
    public void FindHeaderEnd_CrlfSeparator_ReturnsHeaderBoundary()
    {
        byte[] article = Encoding.ASCII.GetBytes("Path: test\r\nSubject: hi\r\n\r\nBody\r\n");

        int headerEnd = ArticleByteScanSimd.FindHeaderEnd(article);
        int bodyStart = ArticleByteScanSimd.FindBodyStart(article);

        Assert.That(headerEnd, Is.EqualTo(25));
        Assert.That(bodyStart, Is.EqualTo(27));
        Assert.That(article[bodyStart], Is.EqualTo((byte)'B'));
    }

    /// <summary>
    /// Verifies <see cref="ArticleByteScanSimd.FindHeaderEnd"/> locates <c>\n\n</c> separators.
    /// </summary>
    [Test]
    public void FindHeaderEnd_LfSeparator_ReturnsHeaderBoundary()
    {
        byte[] article = Encoding.ASCII.GetBytes("Path: test\nSubject: hi\n\nBody\n");

        int headerEnd = ArticleByteScanSimd.FindHeaderEnd(article);
        int bodyStart = ArticleByteScanSimd.FindBodyStart(article);

        Assert.That(headerEnd, Is.EqualTo(23));
        Assert.That(bodyStart, Is.EqualTo(24));
        Assert.That(article[bodyStart], Is.EqualTo((byte)'B'));
    }

    /// <summary>
    /// Verifies missing separators return <c>-1</c>.
    /// </summary>
    [Test]
    public void FindHeaderSeparator_Missing_ReturnsNegativeOne()
    {
        byte[] article = Encoding.ASCII.GetBytes("Path: test\r\nSubject: hi\r\n");

        (int headerEnd, int bodyStart) = ArticleByteScanSimd.FindHeaderSeparator(article);

        Assert.That(headerEnd, Is.EqualTo(-1));
        Assert.That(bodyStart, Is.EqualTo(-1));
    }

    /// <summary>
    /// Verifies <see cref="ArticleByteScanSimd.IndexOfLineFeed"/> finds the first line feed in range.
    /// </summary>
    [Test]
    public void IndexOfLineFeed_FindsFirstNewlineInRange()
    {
        byte[] article = Encoding.ASCII.GetBytes("abc\ndef\nghi");

        int index = ArticleByteScanSimd.IndexOfLineFeed(article, 1, article.Length);

        Assert.That(index, Is.EqualTo(3));
        Assert.That(article[index], Is.EqualTo((byte)'\n'));
    }

    /// <summary>
    /// Verifies <see cref="ArticleByteScanSimd.StartsWithAsciiIgnoreCase"/> matches case-insensitively.
    /// </summary>
    [Test]
    public void StartsWithAsciiIgnoreCase_MatchesCaseInsensitivePrefix()
    {
        ReadOnlySpan<byte> line = "CONTENT-TYPE: text/plain"u8;

        Assert.That(ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: "u8), Is.True);
        Assert.That(ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, "Content-Type: multipart"u8), Is.False);
    }

    /// <summary>
    /// Verifies <see cref="ArticleByteScanSimd.ToLowerAscii"/> folds uppercase ASCII and leaves other bytes unchanged.
    /// </summary>
    [Test]
    public void ToLowerAscii_FoldsUppercaseOnly()
    {
        Assert.That(ArticleByteScanSimd.ToLowerAscii((byte)'A'), Is.EqualTo((byte)'a'));
        Assert.That(ArticleByteScanSimd.ToLowerAscii((byte)'Z'), Is.EqualTo((byte)'z'));
        Assert.That(ArticleByteScanSimd.ToLowerAscii((byte)'a'), Is.EqualTo((byte)'a'));
        Assert.That(ArticleByteScanSimd.ToLowerAscii((byte)'1'), Is.EqualTo((byte)'1'));
    }

    /// <summary>
    /// Verifies case-insensitive prefix matching through the Vector128 SIMD path (prefix length 16–31 bytes).
    /// </summary>
    [Test]
    public void StartsWithAsciiIgnoreCase_Vector128Path_FoldsUppercaseLine()
    {
        ReadOnlySpan<byte> line = "CONTENT-TYPE: BINARYPAYLOAD"u8;
        ReadOnlySpan<byte> prefix = "content-type: binary"u8;

        Assert.That(prefix.Length, Is.GreaterThanOrEqualTo(16).And.LessThan(32));
        Assert.That(ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, prefix), Is.True);
    }

    /// <summary>
    /// Verifies case-insensitive prefix matching through the Vector256 SIMD path (prefix length at least 32 bytes).
    /// </summary>
    [Test]
    public void StartsWithAsciiIgnoreCase_Vector256Path_FoldsUppercaseLine()
    {
        ReadOnlySpan<byte> line = "CONTENT-TYPE: APPLICATION/OCTET-STREAM EXTRA"u8;
        ReadOnlySpan<byte> prefix = "content-type: application/octet-"u8;

        Assert.That(prefix.Length, Is.GreaterThanOrEqualTo(32));
        Assert.That(ArticleByteScanSimd.StartsWithAsciiIgnoreCase(line, prefix), Is.True);
    }
}
