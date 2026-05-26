// <copyright file="HostParsingUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Networking;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="HostParsingUtilities"/>.
/// </summary>
[TestFixture]
public sealed class HostParsingUtilitiesTests
{
    /// <summary>
    /// Verifies port suffix detection on hostnames.
    /// </summary>
    [Test]
    public void HasPortSuffix_HostnameWithPort_ReturnsTrue()
    {
        Assert.That(HostParsingUtilities.HasPortSuffix("rabbit.local:5672"), Is.True);
    }

    /// <summary>
    /// Verifies bare hostnames have no port suffix.
    /// </summary>
    [Test]
    public void HasPortSuffix_BareHostname_ReturnsFalse()
    {
        Assert.That(HostParsingUtilities.HasPortSuffix("rabbit.local"), Is.False);
    }

    /// <summary>
    /// Verifies IPv6 bracket stripping.
    /// </summary>
    [Test]
    public void StripIPv6Brackets_BracketedAddress_ReturnsInner()
    {
        Assert.That(HostParsingUtilities.StripIPv6Brackets("[2001:db8::1]"), Is.EqualTo("2001:db8::1"));
    }
}
