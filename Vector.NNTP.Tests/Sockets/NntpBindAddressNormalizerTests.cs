// <copyright file="NntpBindAddressNormalizerTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Tests.Sockets;

/// <summary>
/// Tests for <see cref="NntpBindAddressNormalizer"/>.
/// </summary>
[TestFixture]
public sealed class NntpBindAddressNormalizerTests
{
    /// <summary>
    /// Verifies empty and wildcard IPv4 bind values map to <see cref="IPAddress.Any"/>.
    /// </summary>
    /// <param name="address">Configured bind text.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("*")]
    public void TryResolveIpv4BindAddress_EmptyOrWildcard_ReturnsAny(string? address)
    {
        Assert.That(NntpBindAddressNormalizer.TryResolveIpv4BindAddress(address, out IPAddress ip), Is.True);
        Assert.That(ip, Is.EqualTo(IPAddress.Any));
    }

    /// <summary>
    /// Verifies a specific IPv4 literal resolves correctly.
    /// </summary>
    [Test]
    public void TryResolveIpv4BindAddress_SpecificLiteral_ReturnsAddress()
    {
        Assert.That(
            NntpBindAddressNormalizer.TryResolveIpv4BindAddress("198.18.0.66", out IPAddress ip),
            Is.True);
        Assert.That(ip, Is.EqualTo(IPAddress.Parse("198.18.0.66")));
        Assert.That(ip.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
    }

    /// <summary>
    /// Verifies IPv6 literals are rejected for IPv4 bind configuration.
    /// </summary>
    [Test]
    public void TryResolveIpv4BindAddress_Ipv6Literal_ReturnsFalse()
    {
        Assert.That(
            NntpBindAddressNormalizer.TryResolveIpv4BindAddress("::1", out IPAddress ip),
            Is.False);
        Assert.That(ip, Is.EqualTo(IPAddress.Any));
    }

    /// <summary>
    /// Verifies invalid IPv4 bind text is rejected.
    /// </summary>
    [Test]
    public void TryResolveIpv4BindAddress_InvalidText_ReturnsFalse()
    {
        Assert.That(
            NntpBindAddressNormalizer.TryResolveIpv4BindAddress("not-an-ip", out _),
            Is.False);
    }

    /// <summary>
    /// Verifies empty IPv6 bind configuration disables the IPv6 listener.
    /// </summary>
    /// <param name="address">Configured bind text.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void TryResolveIpv6BindAddress_Empty_ReturnsFalse(string? address)
    {
        Assert.That(NntpBindAddressNormalizer.TryResolveIpv6BindAddress(address, out IPAddress? ip), Is.False);
        Assert.That(ip, Is.Null);
    }

    /// <summary>
    /// Verifies wildcard IPv6 bind values map to <see cref="IPAddress.IPv6Any"/>.
    /// </summary>
    /// <param name="address">Configured bind text.</param>
    [TestCase("*")]
    [TestCase("::")]
    public void TryResolveIpv6BindAddress_Wildcard_ReturnsIpv6Any(string address)
    {
        Assert.That(NntpBindAddressNormalizer.TryResolveIpv6BindAddress(address, out IPAddress? ip), Is.True);
        Assert.That(ip, Is.EqualTo(IPAddress.IPv6Any));
    }

    /// <summary>
    /// Verifies a specific IPv6 literal resolves correctly.
    /// </summary>
    [Test]
    public void TryResolveIpv6BindAddress_SpecificLiteral_ReturnsAddress()
    {
        const string literal = "2c0f:f030:1280:101:198:18:0:66";
        Assert.That(
            NntpBindAddressNormalizer.TryResolveIpv6BindAddress(literal, out IPAddress? ip),
            Is.True);
        Assert.That(ip, Is.EqualTo(IPAddress.Parse(literal)));
        Assert.That(ip!.AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
    }

    /// <summary>
    /// Verifies IPv4 literals are rejected for IPv6 bind configuration.
    /// </summary>
    [Test]
    public void TryResolveIpv6BindAddress_Ipv4Literal_ReturnsFalse()
    {
        Assert.That(
            NntpBindAddressNormalizer.TryResolveIpv6BindAddress("198.18.0.66", out IPAddress? ip),
            Is.False);
        Assert.That(ip, Is.Null);
    }

    /// <summary>
    /// Verifies startup validation accepts a valid dual-stack bind configuration.
    /// </summary>
    [Test]
    public void ValidateBindAddresses_ValidDualStack_ReturnsNull()
    {
        ValidateOptionsResult? bind4 = NntpBindAddressNormalizer.ValidateBindAddress("198.18.0.66");
        ValidateOptionsResult? bind6 = NntpBindAddressNormalizer.ValidateBindAddress6("2c0f:f030:1280:101:198:18:0:66");

        Assert.That(bind4, Is.Null);
        Assert.That(bind6, Is.Null);
    }

    /// <summary>
    /// Verifies startup validation rejects an IPv4 literal in <see cref="NntpServerOptions.BindAddress6"/>.
    /// </summary>
    [Test]
    public void ValidateBindAddress6_Ipv4Literal_Fails()
    {
        ValidateOptionsResult? result = NntpBindAddressNormalizer.ValidateBindAddress6("198.18.0.66");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("BindAddress6"));
    }
}
