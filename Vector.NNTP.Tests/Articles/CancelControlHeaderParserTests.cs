// <copyright file="CancelControlHeaderParserTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Text;
using Vector.NNTP.Articles.Logging;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="CancelControlHeaderParser"/>.
/// </summary>
[TestFixture]
public sealed class CancelControlHeaderParserTests
{
    /// <summary>
    /// Verifies cancel target Message-ID extraction.
    /// </summary>
    [Test]
    public void TryParseCancelTarget_StandardCancelHeader_ReturnsTarget()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Control: cancel <m070725@foo.com>\r\nMessage-ID: <cancel.4066@foo.com>\r\n\r\n");

        Assert.That(
            CancelControlHeaderParser.TryParseCancelTarget(article, out string target),
            Is.True);
        Assert.That(target, Is.EqualTo("<m070725@foo.com>"));
    }

    /// <summary>
    /// Verifies missing cancel headers return false.
    /// </summary>
    [Test]
    public void TryParseCancelTarget_NoControlHeader_ReturnsFalse()
    {
        byte[] article = Encoding.ASCII.GetBytes(
            "Message-ID: <a@b>\r\n\r\n");

        Assert.That(CancelControlHeaderParser.TryParseCancelTarget(article, out _), Is.False);
    }
}
