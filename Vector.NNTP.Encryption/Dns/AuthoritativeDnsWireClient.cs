//-----------------------------------------------------------------------
// <copyright file="AuthoritativeDnsWireClient.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------
// Minimal UDP/TCP DNS TXT client for ACME DNS-01 propagation checks (RFC 1035, RFC 7766 TCP framing).

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Vector.NNTP.Utilities.Dns;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Stateless helpers to send TXT queries directly to one authoritative nameserver (UDP, then TCP when needed).
    /// </summary>
    internal static class AuthoritativeDnsWireClient
    {
        /// <summary>
        /// The receive timeout in milliseconds.
        /// </summary>
        private const int ReceiveTimeoutMs = 5_000;

        /// <summary>
        /// The size of the DNS header.
        /// </summary>
        private const int DnsHeaderSize = 12;

        /// <summary>
        /// Maximum pooled UDP clients (aligned with propagation probe parallelism).
        /// </summary>
        private const int UdpPoolMaxSize = 8;

        /// <summary>
        /// The DNS flag for truncated responses.
        /// </summary>
        private const ushort DnsFlagTruncated = 0x0200;

        /// <summary>
        /// Bounded pool of reusable <see cref="UdpClient"/> instances to amortise socket creation during poll loops.
        /// </summary>
        private static readonly ConcurrentBag<UdpClient> UdpPool = new();

        /// <summary>
        /// Queries TXT for <paramref name="recordName"/> at <paramref name="nameserver"/> and returns whether any answer
        /// payload equals <paramref name="expectedTxt"/>.
        /// </summary>
        /// <param name="nameserver">The nameserver to query.</param>
        /// <param name="recordName">The name of the record to query.</param>
        /// <param name="expectedTxt">Expected TXT bytes (ASCII challenge digest).</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><see langword="true"/> when a matching TXT record is present.</returns>
        internal static async Task<bool> QueryTxtContainsAsync(
            IPAddress nameserver,
            string recordName,
            ReadOnlyMemory<byte> expectedTxt,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(nameserver);
            byte[] queryPacket = DnsWireQueryBuilder.Build(recordName, DnsWireRecordTypes.Txt, out ushort queryId);
            byte[]? udpResponse = await TryUdpQueryAsync(nameserver, queryPacket, cancellationToken).ConfigureAwait(false);
            if (udpResponse is not null &&
                DnsWireTxtResponseParser.ResponseContainsTxt(udpResponse, queryId, expectedTxt.Span))
            {
                return true;
            }

            if (udpResponse is null || ShouldRetryTxtOverTcp(udpResponse))
            {
                byte[]? tcpResponse = await TryTcpQueryAsync(nameserver, queryPacket, cancellationToken).ConfigureAwait(false);
                if (tcpResponse is not null &&
                    DnsWireTxtResponseParser.ResponseContainsTxt(tcpResponse, queryId, expectedTxt.Span))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Queries TXT for <paramref name="recordName"/> at <paramref name="nameserver"/> using UDP, then TCP if truncated or empty answers.
        /// </summary>
        /// <param name="nameserver">The nameserver to query.</param>
        /// <param name="recordName">The name of the record to query.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The list of TXT records.</returns>
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

        /// <summary>
        /// Parses TXT answers from a response (shared with recursive fallback path).
        /// </summary>
        /// <param name="buffer">The response buffer.</param>
        /// <param name="expectedId">The expected ID of the response.</param>
        /// <returns>The list of TXT records.</returns>
        internal static List<string> ParseTxtResponse(byte[] buffer, ushort expectedId)
        {
            return DnsWireTxtResponseParser.ParseTxtResponseStrings(buffer, expectedId);
        }

        /// <summary>
        /// Determines if the TXT query should be retried over TCP.
        /// </summary>
        /// <param name="udpResponse">The UDP response.</param>
        /// <returns><see langword="true"/> if the TXT query should be retried over TCP; otherwise <see langword="false"/>.</returns>
        private static bool ShouldRetryTxtOverTcp(byte[] udpResponse)
        {
            if (udpResponse.Length < DnsHeaderSize)
            {
                return true;
            }

            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(udpResponse.AsSpan(2));
            return (flags & DnsFlagTruncated) != 0;
        }

        /// <summary>
        /// Determines if the TXT query should be retried over TCP.
        /// </summary>
        /// <param name="udpResponse">The UDP response.</param>
        /// <param name="parsedTxt">The parsed TXT records.</param>
        /// <returns><see langword="true"/> if the TXT query should be retried over TCP; otherwise <see langword="false"/>.</returns>
        private static bool ShouldRetryTxtOverTcp(byte[] udpResponse, List<string> parsedTxt)
        {
            return ShouldRetryTxtOverTcp(udpResponse) || parsedTxt.Count == 0;
        }

        /// <summary>
        /// Tries to send a UDP query to the nameserver.
        /// </summary>
        /// <param name="nameserver">The nameserver to query.</param>
        /// <param name="queryPacket">The query packet to send.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response from the nameserver; <see langword="null"/> if the query failed.</returns>
        private static async Task<byte[]?> TryUdpQueryAsync(IPAddress nameserver, byte[] queryPacket, CancellationToken cancellationToken)
        {
            UdpClient udp = RentUdpClient(nameserver.AddressFamily);
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ReceiveTimeoutMs);

            try
            {
                _ = await udp.SendAsync(queryPacket, new IPEndPoint(nameserver, 53), timeoutCts.Token).ConfigureAwait(false);
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
            finally
            {
                ReturnUdpClient(udp);
            }
        }

        /// <summary>
        /// Rents a pooled UDP client for the given address family.
        /// </summary>
        /// <param name="family">Address family for the query.</param>
        /// <returns>A UDP client ready for send/receive.</returns>
        private static UdpClient RentUdpClient(AddressFamily family)
        {
            while (UdpPool.TryTake(out UdpClient? client))
            {
                if (client.Client.AddressFamily == family)
                {
                    return client;
                }

                client.Dispose();
            }

            return new UdpClient(family);
        }

        /// <summary>
        /// Returns a UDP client to the bounded pool or disposes it when the pool is full.
        /// </summary>
        /// <param name="client">Client to return.</param>
        private static void ReturnUdpClient(UdpClient client)
        {
            if (UdpPool.Count < UdpPoolMaxSize)
            {
                UdpPool.Add(client);
            }
            else
            {
                client.Dispose();
            }
        }

        /// <summary>
        /// Tries to send a TCP query to the nameserver.
        /// </summary>
        /// <param name="nameserver">The nameserver to query.</param>
        /// <param name="queryPacket">The query packet to send.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response from the nameserver; <see langword="null"/> if the query failed.</returns>
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
                byte[] lengthPrefix = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)qLen);
                await stream.WriteAsync(lengthPrefix.AsMemory(0, 2), timeoutCts.Token).ConfigureAwait(false);
                await stream.WriteAsync(queryPacket, timeoutCts.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

                byte[] lenBuf = new byte[2];
                await stream.ReadExactlyAsync(lenBuf.AsMemory(0, 2), timeoutCts.Token).ConfigureAwait(false);
                int msgLen = BinaryPrimitives.ReadUInt16BigEndian(lenBuf);
                if (msgLen is <= 0 or > 65535)
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
    }
}
