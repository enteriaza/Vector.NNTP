// <copyright file="DnsWireQueryBuilder.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// DnsWireQueryBuilder.cs -- Builds minimal RFC 1035 DNS queries (single-question).

using System.Buffers.Binary;

namespace Vector.NNTP.Utilities.Dns;

/// <summary>
/// Builds a minimal RFC 1035 DNS query (single question).
/// </summary>
public static class DnsWireQueryBuilder
{
    /// <summary>
    /// IN class constant (RFC 1035).
    /// </summary>
    public const ushort DnsClassIn = 1;

    private const int DnsHeaderSize = 12;
    private const int QuestionSuffixSize = 4;
    private const int MaxStackAllocQuerySize = DnsHeaderSize + DnsWireFormatUtilities.MaxWireNameLength + QuestionSuffixSize;

    /// <summary>
    /// Builds a DNS query packet for the given QNAME and QTYPE.
    /// </summary>
    /// <param name="name">QNAME in dotted ASCII form. A trailing dot is tolerated and trimmed.</param>
    /// <param name="qtype">DNS query type (e.g. TXT, NS, A).</param>
    /// <param name="queryId">Random query identifier echoed in the response.</param>
    /// <param name="recursionDesired">When true, sets the RD bit for recursive resolvers; false for authoritative targets.</param>
    /// <returns>DNS query bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is invalid for DNS wire encoding.</exception>
    public static byte[] Build(string name, ushort qtype, out ushort queryId, bool recursionDesired = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        name = name.TrimEnd('.');

        if (!DnsWireFormatUtilities.TryValidateDnsName(name, out string? error))
        {
            throw new ArgumentException(error, nameof(name));
        }

        queryId = (ushort)Random.Shared.Next(ushort.MaxValue + 1);

        int qnameLength = DnsWireFormatUtilities.ComputeWireNameLength(name);
        int packetLength = DnsHeaderSize + qnameLength + QuestionSuffixSize;

        Span<byte> span = packetLength <= MaxStackAllocQuerySize
            ? stackalloc byte[MaxStackAllocQuerySize]
            : new byte[packetLength];

        BinaryPrimitives.WriteUInt16BigEndian(span, queryId);
        ushort flags = recursionDesired ? (ushort)0x0100 : (ushort)0;
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], flags);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 1);

        int offset = DnsHeaderSize;
        offset += DnsWireFormatUtilities.EncodeDnsName(name, span[offset..]);

        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], qtype);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], DnsClassIn);

        return span[..packetLength].ToArray();
    }
}
