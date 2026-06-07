// <copyright file="ArticleRangeOrMessageIdSyntaxTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: unit tests for shared article range and Message-ID selector parsing.

using Vector.NNTP.Sockets.Protocol;

namespace Vector.NNTP.Tests.Sockets;

/// <summary>
/// Unit tests for <see cref="ArticleRangeOrMessageIdSyntax"/> and <see cref="ArticleSelectorSyntax"/>.
/// </summary>
[TestFixture]
public sealed class ArticleRangeOrMessageIdSyntaxTests
{
    /// <summary>
    /// Verifies valid range and Message-ID selectors parse successfully.
    /// </summary>
    /// <param name="argument">Selector argument.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("1")]
    [TestCase("1-5")]
    [TestCase("<a@b.c>")]
    public void TryParse_ValidSelectors_ReturnsTrue(string? argument)
    {
        Assert.That(
            ArticleRangeOrMessageIdSyntax.TryParse(argument, out _, out _, out _),
            Is.True);
    }

    /// <summary>
    /// Verifies invalid range and Message-ID selectors are rejected.
    /// </summary>
    /// <param name="argument">Selector argument.</param>
    [TestCase("foo")]
    [TestCase("<a@>")]
    [TestCase("<a..b@host>")]
    [TestCase("1-")]
    [TestCase("abc-def")]
    public void TryParse_InvalidSelectors_ReturnsFalse(string argument)
    {
        Assert.That(
            ArticleRangeOrMessageIdSyntax.TryParse(argument, out _, out _, out _),
            Is.False);
    }

    /// <summary>
    /// Verifies a valid Message-ID selector populates <c>messageId</c>.
    /// </summary>
    [Test]
    public void TryParse_ValidMessageId_SetsMessageId()
    {
        const string messageId = "<abc@example.com>";
        Assert.That(
            ArticleRangeOrMessageIdSyntax.TryParse(messageId, out long? low, out long? high, out string? parsed),
            Is.True);
        Assert.That(low, Is.Null);
        Assert.That(high, Is.Null);
        Assert.That(parsed, Is.EqualTo(messageId));
    }

    /// <summary>
    /// Verifies a numeric range populates low and high bounds.
    /// </summary>
    [Test]
    public void TryParse_Range_SetsBounds()
    {
        Assert.That(
            ArticleRangeOrMessageIdSyntax.TryParse("10-20", out long? low, out long? high, out string? messageId),
            Is.True);
        Assert.That(low, Is.EqualTo(10));
        Assert.That(high, Is.EqualTo(20));
        Assert.That(messageId, Is.Null);
    }

    /// <summary>
    /// Verifies article selector syntax rejects hyphenated ranges.
    /// </summary>
    [Test]
    public void ArticleSelectorSyntax_Range_ReturnsFalse()
    {
        Assert.That(
            ArticleSelectorSyntax.TryParse("1-5", out _, out _),
            Is.False);
    }

    /// <summary>
    /// Verifies article selector syntax accepts a single article number.
    /// </summary>
    [Test]
    public void ArticleSelectorSyntax_SingleNumber_ReturnsTrue()
    {
        Assert.That(
            ArticleSelectorSyntax.TryParse("42", out long? number, out string? messageId),
            Is.True);
        Assert.That(number, Is.EqualTo(42));
        Assert.That(messageId, Is.Null);
    }
}
