// <copyright file="CertificateOrderDomainBuilderTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Encryption.Acme;
using Vector.NNTP.Encryption.Configuration;

namespace Vector.NNTP.Tests.Encryption.Acme;

/// <summary>
/// Unit tests for <see cref="CertificateOrderDomainBuilder"/>.
/// </summary>
[TestFixture]
public sealed class CertificateOrderDomainBuilderTests
{
    /// <summary>
    /// WildcardOnly mode returns a single wildcard identifier.
    /// </summary>
    [Test]
    public void BuildOrderDomains_WildcardOnly_ReturnsSingleWildcard()
    {
        LetsEncryptOptions options = new()
        {
            OrderMode = CertificateOrderMode.WildcardOnly,
            DomainNames = ["*.example.com"],
        };

        string[] domains = CertificateOrderDomainBuilder.BuildOrderDomains(options);

        Assert.That(domains, Has.Length.EqualTo(1));
        Assert.That(domains[0], Is.EqualTo("*.example.com"));
    }

    /// <summary>
    /// WildcardAndHostname mode requires wildcard plus explicit hostname.
    /// </summary>
    [Test]
    public void BuildOrderDomains_WildcardAndHostname_ReturnsBothEntries()
    {
        LetsEncryptOptions options = new()
        {
            OrderMode = CertificateOrderMode.WildcardAndHostname,
            DomainNames = ["*.example.com", "nntp.example.com"],
        };

        string[] domains = CertificateOrderDomainBuilder.BuildOrderDomains(options);

        Assert.That(domains, Has.Length.EqualTo(2));
        Assert.That(domains[0], Is.EqualTo("*.example.com"));
        Assert.That(domains[1], Is.EqualTo("nntp.example.com"));
    }

    /// <summary>
    /// SingleHostname mode rejects wildcard identifiers.
    /// </summary>
    [Test]
    public void BuildOrderDomains_SingleHostname_RejectsWildcard()
    {
        LetsEncryptOptions options = new()
        {
            OrderMode = CertificateOrderMode.SingleHostname,
            DomainNames = ["*.example.com"],
        };

        Assert.Throws<InvalidOperationException>(() => CertificateOrderDomainBuilder.BuildOrderDomains(options));
    }

    /// <summary>
    /// Duplicate domain entries are rejected.
    /// </summary>
    [Test]
    public void BuildOrderDomains_DuplicateDomains_Throws()
    {
        LetsEncryptOptions options = new()
        {
            OrderMode = CertificateOrderMode.SingleHostname,
            DomainNames = ["a.example.com", "a.example.com"],
        };

        Assert.Throws<InvalidOperationException>(() => CertificateOrderDomainBuilder.BuildOrderDomains(options));
    }
}
