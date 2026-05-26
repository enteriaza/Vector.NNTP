//-----------------------------------------------------------------------
// <copyright file="AuthoritativeDnsWireClient.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------
// Minimal UDP/TCP DNS TXT client for ACME DNS-01 propagation checks (RFC 1035, RFC 7766 TCP framing).

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Vector.NNTP.Encryption.Dns;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Stateless helpers to send TXT queries directly to one authoritative nameserver (UDP, then TCP when needed).
    /// </summary>
        internal static class AuthoritativeDnsWireClient
    {
        private const int ReceiveTimeoutMs = 5_000;
        private const int DnsHeaderSize = 12;
        private const int RrFixedFieldsSize = 10;
        private const int MaxCompressionPointerHops = 128;
        private const int QuestionSuffixSize = 4;
        private const ushort DnsFlagTruncated = 0x0200;

        /// <summary>
        /// Queries TXT for <paramref name="recordName"/> at <paramref name="nameserver"/> using UDP, then TCP if truncated or empty answers.
        /// </summary>
        internal static async Task<List<string>> QueryTxtAsync(IPAddress nameserver, string recordName, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(nameserver);
            byte[] queryPacket = DnsWireQueryBuilder.Build(recordName, DnsWireRecordTypes.Txt, out ushort queryId);
            byte[]? udpResponse = await TryUdpQueryAsync(nameserver, queryPacket, cancellationToken).ConfigureAwait(false);
            List<string> results = udpResponse is null ? [] : ParseTxtResponse(udpResponse, queryId);
            if (udpResponse is null || ShouldRetryTxtOverTcp(udpResponse, results))
            {
                byte[]? tcpResponse = await TryTcpQueryAsync(nameserver, queryPacket, cancellationToken).ConfigureAwait(false);
                if (tcpResponse is not null)
                {
                    List<string> tcpParsed = ParseTxtResponse(tcpResponse, queryId);
                    if (tcpParsed.Count > 0)
                    {
                        return tcpParsed;
                    }
                }
            }

            return results;
        }

        private static bool ShouldRetryTxtOverTcp(byte[] udpResponse, List<string> parsedTxt)
        {
            if (udpResponse.Length < DnsHeaderSize)
            {
                return true;
            }

            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(udpResponse.AsSpan(2));
            bool truncated = (flags & DnsFlagTruncated) != 0;
            return truncated || parsedTxt.Count == 0;
        }

        private static async Task<byte[]?> TryUdpQueryAsync(IPAddress nameserver, byte[] queryPacket, CancellationToken cancellationToken)
        {
            using UdpClient udp = new(nameserver.AddressFamily);
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ReceiveTimeoutMs);

            try
            {
                await udp.SendAsync(queryPacket, new IPEndPoint(nameserver, 53), timeoutCts.Token).ConfigureAwait(false);
                UdpReceiveResult result = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
                return result.Buffer;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (SocketException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        private static async Task<byte[]?> TryTcpQueryAsync(
            IPAddress nameserver,
            byte[] queryPacket,
            CancellationToken cancellationToken)
        {
            using TcpClient tcp = new();
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ReceiveTimeoutMs);

            try
            {
                await tcp.ConnectAsync(nameserver, 53, timeoutCts.Token).ConfigureAwait(false);
                NetworkStream stream = tcp.GetStream();
                int qLen = queryPacket.Length;
                byte[] lengthPrefix = new byte[2 + qLen];
                BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix.AsSpan(0, 2), (ushort)qLen);
                queryPacket.CopyTo(lengthPrefix.AsSpan(2));
                await stream.WriteAsync(lengthPrefix.AsMemory(0, lengthPrefix.Length), timeoutCts.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

                byte[] lenBuf = new byte[2];
                await stream.ReadExactlyAsync(lenBuf.AsMemory(0, 2), timeoutCts.Token).ConfigureAwait(false);
                int msgLen = BinaryPrimitives.ReadUInt16BigEndian(lenBuf);
                if (msgLen <= 0 || msgLen > 65535)
                {
                    return null;
                }

                byte[] response = new byte[msgLen];
                await stream.ReadExactlyAsync(response.AsMemory(0, msgLen), timeoutCts.Token).ConfigureAwait(false);
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (SocketException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Parses TXT answers from a response (shared with recursive fallback path).
        /// </summary>
        internal static List<string> ParseTxtResponse(byte[] buffer, ushort expectedId)
        {
            List<string> results = [];
            if (buffer.Length < DnsHeaderSize)
            {
                return results;
            }

            ReadOnlySpan<byte> span = buffer;
            ushort responseId = BinaryPrimitives.ReadUInt16BigEndian(span);
            if (responseId != expectedId)
            {
                return results;
            }

            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
            if ((flags & 0xFA0F) != 0x8000)
            {
                return results;
            }

            ushort qdCount = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            ushort anCount = BinaryPrimitives.ReadUInt16BigEndian(span[6..]);
            int offset = DnsHeaderSize;

            for (int q = 0; q < qdCount; q++)
            {
                if (!TrySkipName(span, ref offset))
                {
                    return results;
                }

                if (offset + QuestionSuffixSize > span.Length)
                {
                    return results;
                }

                offset += QuestionSuffixSize;
            }

            for (int a = 0; a < anCount; a++)
            {
                if (!TrySkipName(span, ref offset))
                {
                    return results;
                }

                if (offset + RrFixedFieldsSize > span.Length)
                {
                    return results;
                }

                ushort rrType = BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);
                ushort rrClass = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 2)..]);
                ushort rdLength = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 8)..]);
                offset += RrFixedFieldsSize;

                if (offset + rdLength > span.Length)
                {
                    return results;
                }

                if (rrType == DnsWireRecordTypes.Txt && rrClass == DnsWireQueryBuilder.DnsClassIn)
                {
                    string? txtValue = ParseTxtRdata(span, ref offset, rdLength);
                    if (txtValue is not null)
                    {
                        results.Add(txtValue);
                    }
                }
                else
                {
                    offset += rdLength;
                }
            }

            return results;
        }

        private static string? ParseTxtRdata(ReadOnlySpan<byte> span, ref int offset, ushort rdLength)
        {
            int rdEnd = offset + rdLength;
            if (offset >= rdEnd || offset >= span.Length)
            {
                offset = rdEnd;
                return null;
            }

            int firstStrLen = span[offset++];
            if (offset + firstStrLen > span.Length || offset + firstStrLen > rdEnd)
            {
                offset = rdEnd;
                return null;
            }

            if (offset + firstStrLen == rdEnd)
            {
                string result = Encoding.ASCII.GetString(span.Slice(offset, firstStrLen));
                offset = rdEnd;
                return result;
            }

            StringBuilder sb = new(rdLength);
            sb.Append(Encoding.ASCII.GetString(span.Slice(offset, firstStrLen)));
            offset += firstStrLen;

            while (offset < rdEnd)
            {
                if (offset >= span.Length)
                {
                    break;
                }

                int strLen = span[offset++];
                if (offset + strLen > span.Length || offset + strLen > rdEnd)
                {
                    break;
                }

                sb.Append(Encoding.ASCII.GetString(span.Slice(offset, strLen)));
                offset += strLen;
            }

            offset = rdEnd;
            return sb.ToString();
        }

        private static bool TrySkipName(ReadOnlySpan<byte> span, ref int offset)
        {
            int hops = 0;
            while (offset < span.Length)
            {
                if (++hops > MaxCompressionPointerHops)
                {
                    return false;
                }

                byte b = span[offset];
                if (b == 0)
                {
                    offset++;
                    return true;
                }

                if ((b & 0xC0) == 0xC0)
                {
                    if (offset + 2 > span.Length)
                    {
                        return false;
                    }

                    offset += 2;
                    return true;
                }

                if ((b & 0xC0) != 0)
                {
                    return false;
                }

                int advance = 1 + b;
                if (offset + advance > span.Length)
                {
                    return false;
                }

                offset += advance;
            }

            return false;
        }
    }
}
