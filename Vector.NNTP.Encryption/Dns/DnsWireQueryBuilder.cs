//-----------------------------------------------------------------------
// <copyright file="DnsWireQueryBuilder.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------

using System.Buffers.Binary;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Builds a minimal RFC 1035 DNS query (single question; RD controlled by <c>recursionDesired</c>).
    /// </summary>
    internal static class DnsWireQueryBuilder
    {
        internal const ushort DnsClassIn = 1;
        private const int DnsHeaderSize = 12;
        private const int MaxQnameLength = 255;
        private const int MaxLabelLength = 63;
        private const int QuestionSuffixSize = 4;
        private const int MaxStackAllocQuerySize = DnsHeaderSize + MaxQnameLength + 4;

        /// <summary>
        /// Builds a DNS query packet for the given QNAME and QTYPE.
        /// </summary>
        /// <param name="name">QNAME labels (ASCII).</param>
        /// <param name="qtype">DNS query type (e.g. TXT, NS, A).</param>
        /// <param name="queryId">Random query identifier echoed in the response.</param>
        /// <param name="recursionDesired">When true, sets the RD bit for recursive resolvers; false for authoritative targets.</param>
        public static byte[] Build(string name, ushort qtype, out ushort queryId, bool recursionDesired = false)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            name = name.TrimEnd('.');

            queryId = (ushort)Random.Shared.Next(ushort.MaxValue + 1);

            string[] labels = name.Split('.');
            int qnameLength = 1;
            foreach (string label in labels)
            {
                if (label.Length == 0)
                {
                    throw new ArgumentException(
                        $"DNS name '{name}' contains an empty label (consecutive dots or leading dot).",
                        nameof(name));
                }

                if (label.Length > MaxLabelLength)
                {
                    throw new ArgumentException(
                        $"DNS label '{label}' exceeds the maximum length of {MaxLabelLength} bytes (RFC 1035 §2.3.4).",
                        nameof(name));
                }

                if (!DnsAsciiEncoding.IsAscii(label.AsSpan()))
                {
                    throw new ArgumentException(
                        $"DNS label '{label}' contains non-ASCII characters.  DNS names must be pure ASCII (RFC 1035 §2.3.4).",
                        nameof(name));
                }

                qnameLength += 1 + label.Length;
            }

            if (qnameLength > MaxQnameLength)
            {
                throw new ArgumentException(
                    $"DNS name '{name}' exceeds the maximum QNAME length of {MaxQnameLength} bytes (RFC 1035 §3.1).",
                    nameof(name));
            }

            int packetLength = DnsHeaderSize + qnameLength + QuestionSuffixSize;
            Span<byte> span = packetLength <= MaxStackAllocQuerySize
                ? stackalloc byte[MaxStackAllocQuerySize]
                : new byte[packetLength];

            BinaryPrimitives.WriteUInt16BigEndian(span, queryId);
            ushort flags = recursionDesired ? (ushort)0x0100 : (ushort)0;
            BinaryPrimitives.WriteUInt16BigEndian(span[2..], flags);
            BinaryPrimitives.WriteUInt16BigEndian(span[4..], 1);

            int offset = DnsHeaderSize;
            foreach (string label in labels)
            {
                span[offset++] = (byte)label.Length;
                offset += DnsAsciiEncoding.AsciiToSpan(label, span[offset..]);
            }

            span[offset++] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], qtype);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], DnsClassIn);

            return span[..packetLength].ToArray();
        }
    }
}
