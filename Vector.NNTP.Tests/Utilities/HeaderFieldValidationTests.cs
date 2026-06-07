// <copyright file="HeaderFieldValidationTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Unit tests for <see cref="HeaderFieldValidation"/>.
/// </summary>
[TestFixture]
public sealed class HeaderFieldValidationTests
{
    /// <summary>
    /// Verifies valid header names pass validation.
    /// </summary>
    /// <param name="name">Header name.</param>
    [TestCase("Message-ID")]
    [TestCase("Content-Type")]
    [TestCase("X-Trace")]
    public void IsValidHeaderName_ValidNames_ReturnsTrue(string name)
    {
        Assert.That(HeaderFieldValidation.IsValidHeaderName(name), Is.True);
    }

    /// <summary>
    /// Verifies invalid header names are rejected.
    /// </summary>
    /// <param name="name">Header name.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("Bad:Name")]
    [TestCase(" space")]
    public void IsValidHeaderName_InvalidNames_ReturnsFalse(string? name)
    {
        Assert.That(HeaderFieldValidation.IsValidHeaderName(name), Is.False);
    }

    /// <summary>
    /// Verifies folded header bodies with continuation lines are accepted.
    /// </summary>
    [Test]
    public void IsValidHeaderBody_FoldedContinuation_ReturnsTrue()
    {
        const string body = "first line\r\n second line";
        Assert.That(HeaderFieldValidation.IsValidHeaderBody(body), Is.True);
    }

    /// <summary>
    /// Verifies a complete header field line is validated end-to-end.
    /// </summary>
    [Test]
    public void IsValidHeaderField_StandardLine_ReturnsTrue()
    {
        Assert.That(HeaderFieldValidation.IsValidHeaderField("Subject: hello world"), Is.True);
    }

    /// <summary>
    /// Verifies missing space after colon fails validation.
    /// </summary>
    [Test]
    public void IsValidHeaderField_MissingSpaceAfterColon_ReturnsFalse()
    {
        Assert.That(HeaderFieldValidation.IsValidHeaderField("Subject:hello"), Is.False);
    }

    /// <summary>
    /// Verifies byte-span header field validation matches the string overload for a standard line.
    /// </summary>
    [Test]
    public void IsValidHeaderField_ByteSpan_MatchesStringOverload()
    {
        const string line = "Subject: hello world";
        Assert.That(
            HeaderFieldValidation.IsValidHeaderField(line),
            Is.EqualTo(HeaderFieldValidation.IsValidHeaderField("Subject: hello world"u8)));
    }

    /// <summary>
    /// Verifies byte-span validation rejects invalid UTF-8 in the header body.
    /// </summary>
    [Test]
    public void IsValidHeaderField_ByteSpan_InvalidUtf8_ReturnsFalse()
    {
        byte[] line =
        [
            (byte)'S', (byte)'u', (byte)'b', (byte)'j', (byte)'e', (byte)'c', (byte)'t', (byte)':', (byte)' ',
            0xC0, 0x80,
        ];
        Assert.That(HeaderFieldValidation.IsValidHeaderField(line), Is.False);
    }
}
