// <copyright file="DnsWireFormatUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Dns;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="DnsWireFormatUtilities"/> validation and encoding.
/// </summary>
[TestFixture]
public sealed class DnsWireFormatUtilitiesTests
{
    /// <summary>
    /// Verifies a valid ACME-style DNS name passes validation and encodes to a decodable QNAME.
    /// </summary>
    [Test]
    public void TryValidateDnsName_ValidName_EncodesAndRoundTrips()
    {
        const string name = "_acme-challenge.example.com";

        Assert.That(DnsWireFormatUtilities.TryValidateDnsName(name, out string? error), Is.True);
        Assert.That(error, Is.Null);

        int wireLength = DnsWireFormatUtilities.ComputeWireNameLength(name);
        Span<byte> buffer = stackalloc byte[wireLength];
        int written = DnsWireFormatUtilities.EncodeDnsName(name, buffer);

        Assert.That(written, Is.EqualTo(wireLength));

        int offset = 0;
        Assert.That(DnsWireNameReader.TryReadDomainName(buffer, ref offset, out string decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(name));
        Assert.That(offset, Is.EqualTo(written));
    }

    /// <summary>
    /// Verifies empty labels and non-ASCII labels are rejected.
    /// </summary>
    [Test]
    public void TryValidateDnsName_RejectsEmptyLabelAndNonAscii()
    {
        Assert.That(DnsWireFormatUtilities.TryValidateDnsName("example..com", out _), Is.False);
        Assert.That(DnsWireFormatUtilities.TryValidateDnsName("café.example.com", out _), Is.False);
        Assert.That(DnsWireFormatUtilities.TryValidateDnsName(string.Empty, out _), Is.False);
    }

    /// <summary>
    /// Verifies string and span validation overloads agree.
    /// </summary>
    [Test]
    public void TryValidateDnsName_SpanOverload_MatchesStringOverload()
    {
        ReadOnlySpan<char> span = "test.example.com".AsSpan();

        bool stringResult = DnsWireFormatUtilities.TryValidateDnsName(span.ToString(), out string? stringError);
        bool spanResult = DnsWireFormatUtilities.TryValidateDnsName(span, out string? spanError);

        Assert.That(spanResult, Is.EqualTo(stringResult));
        Assert.That(spanError, Is.EqualTo(stringError));
    }
}
