// <copyright file="CloudflareDnsRecordNamingTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Encryption.Acme;

namespace Vector.NNTP.Tests.Encryption.Acme;

/// <summary>
/// Unit tests for <see cref="CloudflareDnsRecordNaming"/>.
/// </summary>
[TestFixture]
public sealed class CloudflareDnsRecordNamingTests
{
    /// <summary>
    /// Wildcard domain apex maps challenge FQDN to zone-relative name.
    /// </summary>
    [Test]
    public void NormalizeTxtRecordNameForApi_WildcardZone_ReturnsRelativeChallengeName()
    {
        string result = CloudflareDnsRecordNaming.NormalizeTxtRecordNameForApi(
            "_acme-challenge.usenet.ninja",
            ["*.usenet.ninja"]);

        Assert.That(result, Is.EqualTo("_acme-challenge"));
    }
}
