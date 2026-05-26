// <copyright file="EncodingUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="EncodingUtilities"/> ASCII encode/decode parity.
/// </summary>
[TestFixture]
public sealed class EncodingUtilitiesTests
{
    /// <summary>
    /// Verifies string and span encode paths produce identical bytes for ASCII input.
    /// </summary>
    [Test]
    public void AsciiToBytes_AndSpanOverload_AgreeForAscii()
    {
        const string value = "dns-label_01";

        byte[] fromString = EncodingUtilities.AsciiToBytes(value);
        Span<byte> fromSpanBuffer = stackalloc byte[value.Length];
        int written = EncodingUtilities.AsciiToSpan(value, fromSpanBuffer);

        Assert.That(written, Is.EqualTo(value.Length));
        Assert.That(fromSpanBuffer[..written].ToArray(), Is.EqualTo(fromString));
    }

    /// <summary>
    /// Verifies non-ASCII input is rejected by strict encode helpers.
    /// </summary>
    [Test]
    public void AsciiToBytes_RejectsNonAscii()
    {
        Assert.Throws<ArgumentException>(() => EncodingUtilities.AsciiToBytes("naïve"));
    }

    /// <summary>
    /// Verifies decode round-trips ASCII bytes to the original string.
    /// </summary>
    [Test]
    public void AsciiToString_RoundTripsAsciiBytes()
    {
        ReadOnlySpan<byte> bytes = "hello"u8;

        string decoded = EncodingUtilities.AsciiToString(bytes);

        Assert.That(decoded, Is.EqualTo("hello"));
    }
}
