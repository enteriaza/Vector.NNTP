// <copyright file="ArticlePathHeaderMutatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Vector.NNTP.Articles.Processing;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="ArticlePathHeaderMutator"/>.
/// </summary>
[TestFixture]
public sealed class ArticlePathHeaderMutatorTests
{
    /// <summary>
    /// Verifies existing <c>Path:</c> headers are prepended with <c>PathAppend!</c>.
    /// </summary>
    [Test]
    public void PrependPathAppend_ExistingPath_PrependsToken()
    {
        const string article = "Path: peer!host\r\nSubject: test\r\n\r\nbody\r\n.\r\n";
        byte[] mutated = ArticlePathHeaderMutator.PrependPathAppend(Encoding.ASCII.GetBytes(article), "nntpd01.example!spool");
        string text = Encoding.ASCII.GetString(mutated);
        Assert.That(text, Does.Contain("Path: nntpd01.example!spool!peer!host"));
    }

    /// <summary>
    /// Verifies a missing <c>Path:</c> header is inserted as the first header line.
    /// </summary>
    [Test]
    public void PrependPathAppend_NoPath_InsertsAtTop()
    {
        const string article = "Message-ID: <a@b>\r\nSubject: test\r\n\r\nbody\r\n";
        byte[] mutated = ArticlePathHeaderMutator.PrependPathAppend(Encoding.ASCII.GetBytes(article), "usenetninja");
        string text = Encoding.ASCII.GetString(mutated);
        Assert.That(text, Does.StartWith("Path: usenetninja\r\n"));
        Assert.That(text.IndexOf("Path: usenetninja", StringComparison.Ordinal), Is.LessThan(text.IndexOf("Message-ID:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Verifies folded <c>Path:</c> continuation lines are preserved when prepending to the first line.
    /// </summary>
    [Test]
    public void PrependPathAppend_FoldedPathContinuation_PreservesContinuationLine()
    {
        const string article = "Path: a!b!c!\r\n d!e!f\r\nSubject: test\r\n\r\nbody\r\n";
        byte[] mutated = ArticlePathHeaderMutator.PrependPathAppend(Encoding.ASCII.GetBytes(article), "myhost");
        string text = Encoding.ASCII.GetString(mutated);
        Assert.That(text, Does.Contain("Path: myhost!a!b!c!"));
        Assert.That(text, Does.Contain(" d!e!f"));
    }

    /// <summary>
    /// Verifies <c>Path::</c> is not treated as a <c>Path</c> header; a new <c>Path</c> line is inserted at the top.
    /// </summary>
    [Test]
    public void PrependPathAppend_DoubleColonPathField_InsertsNewPathAtTop()
    {
        const string article = "Path:: bad\r\nMessage-ID: <a@b>\r\n\r\nbody\r\n";
        byte[] mutated = ArticlePathHeaderMutator.PrependPathAppend(Encoding.ASCII.GetBytes(article), "myhost");
        string text = Encoding.ASCII.GetString(mutated);
        Assert.That(text, Does.StartWith("Path: myhost\r\n"));
        Assert.That(text, Does.Contain("Path:: bad"));
        Assert.That(text, Does.Not.Contain("Path: myhost!"));
    }

    /// <summary>
    /// Verifies <c>PATH:</c> (any case) is recognized as a <c>Path</c> header.
    /// </summary>
    [Test]
    public void PrependPathAppend_UppercasePath_PrependsToken()
    {
        const string article = "PATH: peer!host\r\n\r\nbody\r\n";
        byte[] mutated = ArticlePathHeaderMutator.PrependPathAppend(Encoding.ASCII.GetBytes(article), "hop");
        string text = Encoding.ASCII.GetString(mutated);
        Assert.That(text, Does.Contain("Path: hop!peer!host"));
    }

    /// <summary>
    /// Verifies empty or whitespace <c>pathAppend</c> throws rather than copying the article.
    /// </summary>
    /// <param name="pathAppend">Hop token under test.</param>
    [TestCase("")]
    [TestCase("   ")]
    public void PrependPathAppend_EmptyHop_ThrowsArgumentException(string pathAppend)
    {
        byte[] article = "Message-ID: <a@b>\r\n\r\n"u8.ToArray();
        Assert.Throws<ArgumentException>(() => ArticlePathHeaderMutator.PrependPathAppend(article, pathAppend));
    }
}
