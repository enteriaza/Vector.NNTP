// <copyright file="ProxyProtocolPreamble.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: HAProxy PROXY protocol v1/v2 preamble parsing.

using System.Buffers.Binary;

namespace Vector.NNTP.Sockets.Proxy
{
    /// <summary>
    /// Parses an optional HAProxy PROXY protocol preamble (v1 text or v2 binary).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This parser is designed for accept-time use. It is hard-bounded and does not allocate based on attacker-controlled
    /// sizes. Any bytes read beyond the preamble must be preserved by the caller for subsequent consumers.
    /// </para>
    /// </remarks>
    internal static class ProxyProtocolPreamble
    {
        private static readonly byte[] V2Signature =
        [
            0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A,
        ];

        /// <summary>
        /// Maximum bytes to read while attempting to parse a PROXY v1 line.
        /// </summary>
        internal const int MaxV1LineBytes = 512;

        /// <summary>
        /// Maximum allowed PROXY v2 frame length (address block + TLVs), excluding the fixed 16-byte header.
        /// </summary>
        internal const int MaxV2FrameLength = 1024;

        /// <summary>
        /// Attempts to parse a PROXY preamble from <paramref name="buffer"/>.
        /// </summary>
        /// <param name="tcpPeer">TCP peer (first hop) endpoint.</param>
        /// <param name="buffer">Initial bytes read from the connection.</param>
        /// <param name="consumed">Number of bytes consumed from <paramref name="buffer"/> for the preamble.</param>
        /// <param name="clientEndPoint">Effective client endpoint when PROXY was present and parsed.</param>
        /// <returns>True when a PROXY preamble was consumed (v1 or v2), false otherwise.</returns>
        internal static bool TryParse(ReadOnlySpan<byte> buffer, IPEndPoint tcpPeer, out int consumed, out IPEndPoint clientEndPoint)
        {
            if (buffer.Length >= V2Signature.Length && buffer.Slice(0, V2Signature.Length).SequenceEqual(V2Signature))
            {
                return TryParseV2(buffer, tcpPeer, out consumed, out clientEndPoint);
            }

            if (buffer.Length >= 6 && buffer[0] == (byte)'P' && buffer[1] == (byte)'R' && buffer[2] == (byte)'O'
                && buffer[3] == (byte)'X' && buffer[4] == (byte)'Y' && buffer[5] == (byte)' ')
            {
                return TryParseV1(buffer, tcpPeer, out consumed, out clientEndPoint);
            }

            consumed = 0;
            clientEndPoint = tcpPeer;
            return false;
        }

        /// <summary>
        /// Attempts to parse a PROXY preamble from a byte array without using ref-like locals in async callers.
        /// </summary>
        /// <param name="buffer">Backing buffer.</param>
        /// <param name="length">Valid byte count in <paramref name="buffer"/>.</param>
        /// <param name="tcpPeer">TCP peer endpoint.</param>
        /// <param name="consumed">Number of bytes consumed for the preamble.</param>
        /// <param name="clientEndPoint">Effective client endpoint.</param>
        /// <returns>True when a PROXY preamble was consumed.</returns>
        internal static bool TryParse(byte[] buffer, int length, IPEndPoint tcpPeer, out int consumed, out IPEndPoint clientEndPoint)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (length <= 0)
            {
                consumed = 0;
                clientEndPoint = tcpPeer;
                return false;
            }

            return TryParse(buffer.AsSpan(0, length), tcpPeer, out consumed, out clientEndPoint);
        }

        private static bool TryParseV1(ReadOnlySpan<byte> buffer, IPEndPoint tcpPeer, out int consumed, out IPEndPoint clientEndPoint)
        {
            clientEndPoint = tcpPeer;
            consumed = 0;

            int lf = buffer.IndexOf((byte)'\n');
            if (lf < 0)
            {
                return false;
            }

            int lineEnd = lf;
            if (lineEnd > 0 && buffer[lineEnd - 1] == (byte)'\r')
            {
                lineEnd--;
            }

            ReadOnlySpan<byte> line = buffer.Slice(0, lineEnd);
            consumed = lf + 1;

            // Minimal v1 text: PROXY TCP4 1.2.3.4 5.6.7.8 1234 5678
            // Tokenize by spaces without allocating.
            if (!TrySkipToken(line, out line))
            {
                return true;
            }

            if (!TrySkipToken(line, out line))
            {
                return true;
            }

            if (!TryReadToken(line, out ReadOnlySpan<byte> srcIpToken, out line))
            {
                return true;
            }

            if (!TrySkipToken(line, out line))
            {
                return true;
            }

            if (!TryReadToken(line, out ReadOnlySpan<byte> srcPortToken, out _))
            {
                return true;
            }

            Span<char> ipChars = stackalloc char[srcIpToken.Length];
            for (int i = 0; i < srcIpToken.Length; i++)
            {
                ipChars[i] = (char)srcIpToken[i];
            }

            Span<char> portChars = stackalloc char[srcPortToken.Length];
            for (int i = 0; i < srcPortToken.Length; i++)
            {
                portChars[i] = (char)srcPortToken[i];
            }

            if (!IPAddress.TryParse(ipChars, out IPAddress? ip))
            {
                return true;
            }

            if (!int.TryParse(portChars, NumberStyles.None, CultureInfo.InvariantCulture, out int port))
            {
                return true;
            }

            if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            {
                return true;
            }

            clientEndPoint = new IPEndPoint(ip, port);
            return true;
        }

        private static bool TryParseV2(ReadOnlySpan<byte> buffer, IPEndPoint tcpPeer, out int consumed, out IPEndPoint clientEndPoint)
        {
            clientEndPoint = tcpPeer;
            consumed = 0;

            // Fixed header is 16 bytes: signature (12) + ver/cmd (1) + fam/proto (1) + len (2)
            if (buffer.Length < 16)
            {
                return false;
            }

            byte verCmd = buffer[12];
            byte ver = (byte)(verCmd >> 4);
            if (ver != 0x2)
            {
                // Signature matched but version is not v2; treat as consumed to avoid confusing downstream consumers.
                consumed = 16;
                return true;
            }

            int frameLen = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(14, 2));
            if (frameLen < 0 || frameLen > MaxV2FrameLength)
            {
                consumed = 16;
                return true;
            }

            int total = 16 + frameLen;
            if (buffer.Length < total)
            {
                return false;
            }

            consumed = total;

            byte cmd = (byte)(verCmd & 0x0F);
            if (cmd == 0x0) // LOCAL
            {
                return true;
            }

            if (cmd != 0x1) // PROXY
            {
                return true;
            }

            byte famProto = buffer[13];
            byte family = (byte)(famProto >> 4);

            ReadOnlySpan<byte> payload = buffer.Slice(16, frameLen);
            if (family == 0x1) // AF_INET
            {
                if (payload.Length < 12)
                {
                    return true;
                }

                IPAddress src = new(payload.Slice(0, 4));
                ushort srcPort = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(8, 2));
                clientEndPoint = new IPEndPoint(src, srcPort);
                return true;
            }

            if (family == 0x2) // AF_INET6
            {
                if (payload.Length < 36)
                {
                    return true;
                }

                IPAddress src = new(payload.Slice(0, 16));
                ushort srcPort = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(32, 2));
                clientEndPoint = new IPEndPoint(src, srcPort);
                return true;
            }

            return true;
        }

        private static bool TryReadToken(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> token, out ReadOnlySpan<byte> remainder)
        {
            buffer = TrimAsciiWhitespaceStart(buffer);
            if (buffer.IsEmpty)
            {
                token = default;
                remainder = default;
                return false;
            }

            int space = buffer.IndexOf((byte)' ');
            if (space < 0)
            {
                token = buffer;
                remainder = ReadOnlySpan<byte>.Empty;
                return true;
            }

            token = buffer.Slice(0, space);
            remainder = buffer.Slice(space + 1);
            return true;
        }

        private static bool TrySkipToken(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> remainder)
        {
            if (!TryReadToken(buffer, out _, out remainder))
            {
                remainder = default;
                return false;
            }

            return true;
        }

        private static ReadOnlySpan<byte> TrimAsciiWhitespaceStart(ReadOnlySpan<byte> buffer)
        {
            int i = 0;
            while (i < buffer.Length)
            {
                byte b = buffer[i];
                if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n')
                {
                    break;
                }

                i++;
            }

            return buffer.Slice(i);
        }
    }
}

