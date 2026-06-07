// <copyright file="NntpNewsLogFormatterTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Articles.Logging;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="NntpNewsLogFormatter"/>.
/// </summary>
[TestFixture]
public sealed class NntpNewsLogFormatterTests
{
    /// <summary>
    /// Fixed timestamp for golden-line assertions.
    /// </summary>
    private static readonly DateTimeOffset SampleTimestamp =
        new(2026, 6, 7, 21, 55, 1, 102, TimeSpan.Zero);

    /// <summary>
    /// Verifies accepted lines use unbracketed size and downstream site placeholder.
    /// </summary>
    [Test]
    public void FormatAccepted_StandardInput_UsesInnShape()
    {
        string line = NntpNewsLogFormatter.FormatAccepted(
            SampleTimestamp,
            "Giganews",
            "<text@example.com>",
            842);

        Assert.That(line, Does.EndWith("+ Giganews <text@example.com> 842 ?"));
    }

    /// <summary>
    /// Verifies junk lines preserve future-compatible shape.
    /// </summary>
    [Test]
    public void FormatJunked_StandardInput_UsesInnShape()
    {
        string line = NntpNewsLogFormatter.FormatJunked(
            SampleTimestamp,
            "Giganews",
            "spam@example.com",
            4096);

        Assert.That(line, Does.EndWith("j Giganews <spam@example.com> 4096 ?"));
    }

    /// <summary>
    /// Verifies rejected lines include sanitized reasons.
    /// </summary>
    [Test]
    public void FormatRejected_StandardInput_IncludesReason()
    {
        string line = NntpNewsLogFormatter.FormatRejected(
            SampleTimestamp,
            "Giganews",
            "msgid@example.com",
            "Invalid header field body for 'Date'.");

        Assert.That(line, Does.Contain("- Giganews <msgid@example.com>"));
        Assert.That(line, Does.Contain("Invalid header field body for 'Date'."));
    }

    /// <summary>
    /// Verifies cancel lines normalize both Message-IDs.
    /// </summary>
    [Test]
    public void FormatCancelProcessed_StandardInput_NormalizesTargets()
    {
        string line = NntpNewsLogFormatter.FormatCancelProcessed(
            SampleTimestamp,
            "Giganews",
            "<cancel.4066@foo.com>",
            "<m070725@foo.com>");

        Assert.That(line, Does.EndWith("c Giganews <cancel.4066@foo.com> Cancelling <m070725@foo.com>"));
    }

    /// <summary>
    /// Verifies Message-ID normalization avoids double brackets.
    /// </summary>
    [Test]
    public void NormalizeMessageId_AlreadyBracketed_DoesNotDoubleWrap()
    {
        Assert.That(
            NntpNewsLogFormatter.NormalizeMessageId("<a@b>"),
            Is.EqualTo("<a@b>"));
        Assert.That(
            NntpNewsLogFormatter.NormalizeMessageId("a@b"),
            Is.EqualTo("<a@b>"));
    }

    /// <summary>
    /// Verifies timestamps render as INN-style local prefixes.
    /// </summary>
    [Test]
    public void FormatTimestamp_UsesInnPattern()
    {
        string formatted = NntpNewsLogFormatter.FormatTimestamp(SampleTimestamp);
        Assert.That(formatted, Does.Match(@"^[A-Z][a-z]{2} \d{2} \d{2}:\d{2}:\d{2}\.\d{3}$"));
    }
}
