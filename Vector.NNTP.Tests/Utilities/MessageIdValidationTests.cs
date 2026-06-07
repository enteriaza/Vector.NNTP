// <copyright file="MessageIdValidationTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Validation;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Unit tests for <see cref="MessageIdValidation"/>.
/// </summary>
[TestFixture]
public sealed class MessageIdValidationTests
{
    /// <summary>
    /// Verifies canonical valid Message-IDs pass validation.
    /// </summary>
    /// <param name="messageId">Candidate Message-ID.</param>
    [TestCase("<a@b>")]
    [TestCase("<a.b@c.d>")]
    [TestCase("<abc123@example.com>")]
    [TestCase("<foo-bar@example.com>")]
    [TestCase("<foo@[127.0.0.1]>")]
    [TestCase("<abc@def.example>")]
    [TestCase("<local.part+tag@host.example.com>")]
    [TestCase("<!#$%&'*+-/=?^_`{|}~@example.com>")]
    [TestCase("<a@[192.0.2.1]>")]
    public void IsValidMessageId_ValidIds_ReturnsTrue(string messageId)
    {
        Assert.That(MessageIdValidation.IsValidMessageId(messageId), Is.True);
    }

    /// <summary>
    /// Verifies invalid Message-IDs are rejected.
    /// </summary>
    /// <param name="messageId">Candidate Message-ID.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("<>")]
    [TestCase("abc@def")]
    [TestCase("<missing-at-sign>")]
    [TestCase("<@example.com>")]
    [TestCase("<@host>")]
    [TestCase("<local@>")]
    [TestCase("<local@host")]
    [TestCase("local@host>")]
    [TestCase("<local@host>>")]
    [TestCase("<local@@host>")]
    [TestCase("<.local@host>")]
    [TestCase("<a.@example.com>")]
    [TestCase("<a..b@example.com>")]
    [TestCase("<a@>")]
    [TestCase("<a@example..com>")]
    [TestCase("<a@example.com")]
    [TestCase("\"foo\"@example.com>")]
    [TestCase("<\"foo\"@example.com>")]
    [TestCase("<\"foo.bar\"@example.com>")]
    [TestCase("<foo.[127.0.0.1]>")]
    public void IsValidMessageId_InvalidIds_ReturnsFalse(string? messageId)
    {
        Assert.That(MessageIdValidation.IsValidMessageId(messageId), Is.False);
    }

    /// <summary>
    /// Verifies RFC 3977 length boundaries at 249, 250, and 251 total octets.
    /// </summary>
    /// <param name="localPartLength">Length of the local-part atom run.</param>
    /// <param name="expectedTotalLength">Expected total Message-ID length.</param>
    /// <param name="expectedValid">Whether the Message-ID should validate.</param>
    [TestCase(245, 249, true)]
    [TestCase(246, 250, true)]
    [TestCase(247, 251, false)]
    public void IsValidMessageId_LengthBoundaries(int localPartLength, int expectedTotalLength, bool expectedValid)
    {
        string messageId = '<' + new string('a', localPartLength) + "@b>";
        Assert.That(messageId.Length, Is.EqualTo(expectedTotalLength));
        Assert.That(MessageIdValidation.IsValidMessageId(messageId), Is.EqualTo(expectedValid));
    }

    /// <summary>
    /// Verifies stripSpaces trims surrounding whitespace before validation.
    /// </summary>
    [Test]
    public void IsValidMessageId_StripSpaces_AcceptsWrappedId()
    {
        Assert.That(MessageIdValidation.IsValidMessageId("  <a@b.c>  ", stripSpaces: true), Is.True);
        Assert.That(MessageIdValidation.IsValidMessageId("  <a@b.c>  ", stripSpaces: false), Is.False);
    }

    /// <summary>
    /// Verifies domain validation accepts dotted hostnames.
    /// </summary>
    [Test]
    public void IsValidDomain_DottedHost_ReturnsTrue()
    {
        Assert.That(MessageIdValidation.IsValidDomain("news.example.com"), Is.True);
    }

    /// <summary>
    /// Verifies domain validation rejects empty atom components.
    /// </summary>
    /// <param name="domain">Domain candidate.</param>
    [TestCase("example..com")]
    [TestCase(".example.com")]
    [TestCase("example.")]
    public void IsValidDomain_EmptyAtomComponents_ReturnsFalse(string domain)
    {
        Assert.That(MessageIdValidation.IsValidDomain(domain), Is.False);
    }

    /// <summary>
    /// Verifies span-based validation does not allocate on the success path.
    /// </summary>
    [Test]
    public void IsValidMessageId_Span_DoesNotAllocate()
    {
        ReadOnlySpan<char> messageId = "<abc123@news.example.com>".AsSpan();
        _ = MessageIdValidation.IsValidMessageId(messageId);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = MessageIdValidation.IsValidMessageId(messageId);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(after - before, Is.LessThanOrEqualTo(64));
    }
}
