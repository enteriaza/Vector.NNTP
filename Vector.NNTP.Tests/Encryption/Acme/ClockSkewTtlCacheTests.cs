// <copyright file="ClockSkewTtlCacheTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Encryption.Acme;

namespace Vector.NNTP.Tests.Encryption.Acme;

/// <summary>
/// Unit tests for <see cref="ClockSkewTtlCache"/>.
/// </summary>
[TestFixture]
public sealed class ClockSkewTtlCacheTests
{
    /// <summary>
    /// Records a successful check and returns true while within TTL.
    /// </summary>
    [Test]
    public void TryHit_AfterRecordSuccess_ReturnsTrueWithinTtl()
    {
        ClockSkewTtlCache.ClearForTests();
        Uri directory = new("https://acme-v02.api.letsencrypt.org/directory");

        ClockSkewTtlCache.RecordSuccess(directory);

        Assert.That(ClockSkewTtlCache.TryHit(directory, TimeSpan.FromMinutes(5)), Is.True);
    }

    /// <summary>
    /// Different directory URIs do not share cache entries.
    /// </summary>
    [Test]
    public void TryHit_DifferentDirectory_ReturnsFalse()
    {
        ClockSkewTtlCache.ClearForTests();
        Uri recorded = new("https://acme-v02.api.letsencrypt.org/directory");
        Uri other = new("https://acme-staging-v02.api.letsencrypt.org/directory");

        ClockSkewTtlCache.RecordSuccess(recorded);

        Assert.That(ClockSkewTtlCache.TryHit(other, TimeSpan.FromMinutes(5)), Is.False);
    }
}
