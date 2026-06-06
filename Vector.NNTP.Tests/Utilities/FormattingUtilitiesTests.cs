// <copyright file="FormattingUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Net;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="FormattingUtilities"/> dictionary formatting overloads.
/// </summary>
[TestFixture]
public sealed class FormattingUtilitiesTests
{
    /// <summary>
    /// Verifies both dictionary overloads produce identical formatted output.
    /// </summary>
    [Test]
    public void FormatKeyValuePairs_DictionaryOverloads_Match()
    {
        Dictionary<string, object> dictionary = new()
        {
            ["host"] = "broker",
            ["port"] = 5672,
            ["enabled"] = true,
        };

        string fromReadOnly = FormattingUtilities.FormatKeyValuePairs((IReadOnlyDictionary<string, object>)dictionary);
        string fromDictionary = FormattingUtilities.FormatKeyValuePairs((IDictionary<string, object>)dictionary);

        Assert.That(fromDictionary, Is.EqualTo(fromReadOnly));
        Assert.That(fromDictionary, Does.Contain("host=broker"));
        Assert.That(fromDictionary, Does.Contain("port=5672"));
    }

    /// <summary>
    /// Verifies IPv4 host:port formatting.
    /// </summary>
    [Test]
    public void FormatHostPort_Ipv4_ReturnsDottedQuadWithPort()
    {
        var address = IPAddress.Parse("127.0.0.1");
        Assert.That(FormattingUtilities.FormatHostPort(address, 119), Is.EqualTo("127.0.0.1:119"));
    }

    /// <summary>
    /// Verifies IPv6 host:port formatting uses RFC 3986 bracket notation.
    /// </summary>
    [Test]
    public void FormatHostPort_Ipv6_ReturnsBracketedAddressWithPort()
    {
        var address = IPAddress.Parse("::1");
        Assert.That(FormattingUtilities.FormatHostPort(address, 119), Is.EqualTo("[::1]:119"));
    }

    /// <summary>
    /// Verifies IPv4-mapped IPv6 addresses are normalised to IPv4 formatting.
    /// </summary>
    [Test]
    public void FormatHostPort_Ipv4Mapped_ReturnsIpv4Form()
    {
        var mapped = IPAddress.Parse("::ffff:192.168.1.1");
        Assert.That(FormattingUtilities.FormatHostPort(mapped, 119), Is.EqualTo("192.168.1.1:119"));
    }

    /// <summary>
    /// Verifies IPv4 connection log prefix wraps address and port in brackets.
    /// </summary>
    [Test]
    public void FormatConnectionLogPrefix_Ipv4_ReturnsBracketedHostPort()
    {
        var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 119);
        Assert.That(FormattingUtilities.FormatConnectionLogPrefix(endPoint), Is.EqualTo("[127.0.0.1:119]"));
    }

    /// <summary>
    /// Verifies IPv6 connection log prefix avoids double-bracketing.
    /// </summary>
    [Test]
    public void FormatConnectionLogPrefix_Ipv6_ReturnsRfc3986Form()
    {
        var endPoint = new IPEndPoint(IPAddress.Parse("::1"), 119);
        Assert.That(FormattingUtilities.FormatConnectionLogPrefix(endPoint), Is.EqualTo("[::1]:119"));
    }

    /// <summary>
    /// Verifies <see cref="FormattingUtilities.FormatIPEndPoint"/> delegates to host:port formatting.
    /// </summary>
    [Test]
    public void FormatIPEndPoint_MatchesFormatHostPort()
    {
        var endPoint = new IPEndPoint(IPAddress.Parse("10.0.0.5"), 43000);
        Assert.That(FormattingUtilities.FormatIPEndPoint(endPoint), Is.EqualTo("10.0.0.5:43000"));
    }
}
