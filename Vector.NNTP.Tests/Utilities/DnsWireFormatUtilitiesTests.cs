// <copyright file="DnsWireFormatUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Buffers.Binary;
using Vector.NNTP.Utilities.Dns;
using Vector.NNTP.Utilities.Encoding;

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

    /// <summary>
    /// Verifies a golden NOERROR TXT response parses and matches expected challenge bytes without string allocation on the hot compare path.
    /// </summary>
    [Test]
    public void DnsWireTxtResponseParser_GoldenAcmeTxtResponse_ParsesAndMatches()
    {
        const string recordName = "_acme-challenge.example.com";
        const string challenge = "abc123-challenge-token";
        byte[] query = DnsWireQueryBuilder.Build(recordName, DnsWireRecordTypes.Txt, out ushort queryId);
        byte[] response = BuildGoldenTxtResponse(queryId, recordName, challenge);
        byte[] expectedBytes = EncodingUtilities.AsciiToBytes(challenge);

        List<byte[]> parsed = [];
        Assert.That(DnsWireTxtResponseParser.TryParseTxtRecords(response, queryId, parsed), Is.True);
        Assert.That(parsed, Has.Count.EqualTo(1));
        Assert.That(parsed[0], Is.EqualTo(expectedBytes));

        Assert.That(DnsWireTxtResponseParser.ResponseContainsTxt(response, queryId, expectedBytes), Is.True);
        Assert.That(DnsWireTxtResponseParser.ResponseContainsTxt(response, queryId, "wrong"u8), Is.False);

        List<string> strings = DnsWireTxtResponseParser.ParseTxtResponseStrings(response, queryId);
        Assert.That(strings, Has.Count.EqualTo(1));
        Assert.That(strings[0], Is.EqualTo(challenge));
    }

    /// <summary>
    /// Verifies <see cref="DnsWireNameSkipper"/> skips inline QNAME labels in a question section.
    /// </summary>
    [Test]
    public void DnsWireNameSkipper_SkipsQuestionName()
    {
        byte[] query = DnsWireQueryBuilder.Build("example.com", DnsWireRecordTypes.Txt, out _);
        int offset = DnsWireFormatUtilities.DnsHeaderSize;
        Assert.That(DnsWireNameSkipper.TrySkipName(query, ref offset), Is.True);
        Assert.That(offset, Is.EqualTo(DnsWireFormatUtilities.DnsHeaderSize + 13));
    }

    /// <summary>
    /// Builds a minimal DNS NOERROR response with one TXT answer for golden parser tests.
    /// </summary>
    /// <param name="queryId">Query identifier echoed in the response header.</param>
    /// <param name="recordName">Owner name for question and answer.</param>
    /// <param name="txtValue">Single-string TXT RDATA payload.</param>
    /// <returns>Wire-format response bytes.</returns>
    private static byte[] BuildGoldenTxtResponse(ushort queryId, string recordName, string txtValue)
    {
        byte[] query = DnsWireQueryBuilder.Build(recordName, DnsWireRecordTypes.Txt, out _);
        int qnameLength = query.Length - DnsWireFormatUtilities.DnsHeaderSize - DnsWireFormatUtilities.QuestionSuffixSize;
        ReadOnlySpan<byte> qname = query.AsSpan(DnsWireFormatUtilities.DnsHeaderSize, qnameLength);

        byte[] rdata = new byte[1 + txtValue.Length];
        rdata[0] = (byte)txtValue.Length;
        _ = EncodingUtilities.AsciiToSpan(txtValue.AsSpan(), rdata.AsSpan(1));

        int responseLength = DnsWireFormatUtilities.DnsHeaderSize
            + qnameLength
            + DnsWireFormatUtilities.QuestionSuffixSize
            + qnameLength
            + 10
            + rdata.Length;

        byte[] response = new byte[responseLength];
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), queryId);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0x8400);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(6, 2), 1);

        int offset = DnsWireFormatUtilities.DnsHeaderSize;
        qname.CopyTo(response.AsSpan(offset));
        offset += qnameLength;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset), DnsWireRecordTypes.Txt);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset + 2), DnsWireQueryBuilder.DnsClassIn);
        offset += DnsWireFormatUtilities.QuestionSuffixSize;

        qname.CopyTo(response.AsSpan(offset));
        offset += qnameLength;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset), DnsWireRecordTypes.Txt);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset + 2), DnsWireQueryBuilder.DnsClassIn);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(offset + 4), 60);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset + 8), (ushort)rdata.Length);
        offset += 10;
        rdata.CopyTo(response.AsSpan(offset));

        return response;
    }
}
