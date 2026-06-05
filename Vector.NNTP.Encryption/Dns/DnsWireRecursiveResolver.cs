//-----------------------------------------------------------------------
// <copyright file="DnsWireRecursiveResolver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe <cknipe@opticnetworks.net>. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
//-----------------------------------------------------------------------

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Vector.NNTP.Utilities.Dns;

namespace Vector.NNTP.Encryption.Dns
{
    /// <summary>
    /// Discovers NS names via UDP to public recursive resolvers, resolves NS hostnames from glue, wire recursive A/AAAA, then OS stub resolver.
    /// </summary>
    internal static class DnsWireRecursiveResolver
    {
        /// <summary>
        /// The UDP timeout in milliseconds.
        /// </summary>
        private const int UdpTimeoutMs = 5_000;

        /// <summary>
        /// The size of the DNS header.
        /// </summary>
        private const int DnsHeaderSize = 12;

        /// <summary>
        /// The size of the question section.
        /// </summary>
        private const int QuestionSuffixSize = 4;

        /// <summary>
        /// The recursive resolvers.
        /// </summary>
        private static readonly IPAddress[] RecursiveResolvers = ResolveRecursiveResolvers();

        /// <summary>
        /// Resolves the recursive resolvers.
        /// </summary>
        /// <returns>The recursive resolvers.</returns>
        private static IPAddress[] ResolveRecursiveResolvers()
        {
            try
            {
                List<IPAddress> servers = [];
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    IPInterfaceProperties props = ni.GetIPProperties();
                    foreach (IPAddress ip in props.DnsAddresses)
                    {
                        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) ||
                            ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback))
                        {
                            continue;
                        }

                        // Avoid duplicates without allocations from HashSet for tiny lists.
                        bool exists = false;
                        for (int i = 0; i < servers.Count; i++)
                        {
                            if (servers[i].Equals(ip))
                            {
                                exists = true;
                                break;
                            }
                        }

                        if (!exists)
                        {
                            servers.Add(ip);
                        }
                    }
                }

                if (servers.Count > 0)
                {
                    return [.. servers];
                }
            }
            catch
            {
                // Best-effort: fall back to a small public resolver set below.
            }

            return
            [
                IPAddress.Parse("1.1.1.1"),
                IPAddress.Parse("8.8.8.8"),
            ];
        }

        /// <summary>
        /// TXT lookup via recursive resolvers (RD=1), used when no authoritative NS list is available.
        /// </summary>
        /// <param name="recordName">The name of the record to resolve.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TXT records of the resolved record.</returns>
        public static async Task<List<string>> QueryTxtRecursiveAsync(string recordName, CancellationToken cancellationToken)
        {
            foreach (IPAddress resolver in RecursiveResolvers)
            {
                byte[] query = DnsWireQueryBuilder.Build(recordName, DnsWireRecordTypes.Txt, out ushort queryId, recursionDesired: true);
                byte[]? response = await SendUdpQueryAsync(resolver, query, cancellationToken).ConfigureAwait(false);
                if (response is null)
                {
                    continue;
                }

                return AuthoritativeDnsWireClient.ParseTxtResponse(response, queryId);
            }

            return [];
        }

        /// <summary>
        /// Resolves distinct authoritative nameserver IPs for the zone that serves <paramref name="recordFqdn"/> (no zone cache).
        /// </summary>
        /// <param name="recordFqdn">The FQDN of the record to resolve.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The addresses of the authoritative NS servers.</returns>
        public static Task<IReadOnlyList<IPAddress>> ResolveAuthoritativeNameServerAddressesAsync(
            string recordFqdn,
            CancellationToken cancellationToken)
        {
            return ResolveAuthoritativeNameServerAddressesAsync(recordFqdn, null, TimeSpan.Zero, cancellationToken);
        }

        /// <summary>
        /// Resolves distinct authoritative nameserver IPs for the zone that serves <paramref name="recordFqdn"/>.
        /// </summary>
        /// <param name="recordFqdn">Challenge record or hostname.</param>
        /// <param name="zoneNsCache">Optional cache keyed by normalized delegation label (e.g. example.com).</param>
        /// <param name="zoneCacheTtl">TTL for cache entries; <see cref="TimeSpan.Zero"/> disables writes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Distinct authoritative NS addresses for the zone that serves <paramref name="recordFqdn"/>.</returns>
        public static async Task<IReadOnlyList<IPAddress>> ResolveAuthoritativeNameServerAddressesAsync(
            string recordFqdn,
            ConcurrentDictionary<string, (IReadOnlyList<IPAddress> Ips, DateTimeOffset ExpiresUtc)>? zoneNsCache,
            TimeSpan zoneCacheTtl,
            CancellationToken cancellationToken)
        {
            (string _, IReadOnlyList<IPAddress> addresses) = await ResolveAuthoritativeZoneAndAddressesAsync(
                recordFqdn,
                zoneNsCache,
                zoneCacheTtl,
                cancellationToken).ConfigureAwait(false);
            return addresses;
        }

        /// <summary>
        /// Resolves authoritative NS IPs and returns the zone label (delegation cut) used for the successful NS set.
        /// </summary>
        /// <param name="recordFqdn">The FQDN of the record to resolve.</param>
        /// <param name="zoneNsCache">The cache of zone NS addresses.</param>
        /// <param name="zoneCacheTtl">The TTL for the cache entries.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The zone name and the addresses of the authoritative NS servers.</returns>
        public static async Task<(string ZoneName, IReadOnlyList<IPAddress> Addresses)> ResolveAuthoritativeZoneAndAddressesAsync(
            string recordFqdn,
            ConcurrentDictionary<string, (IReadOnlyList<IPAddress> Ips, DateTimeOffset ExpiresUtc)>? zoneNsCache,
            TimeSpan zoneCacheTtl,
            CancellationToken cancellationToken)
        {
            string cursor = recordFqdn.TrimEnd('.');
            while (cursor.Contains('.', StringComparison.Ordinal))
            {
                string zoneKey = NormalizeDnsName(cursor);
                if (zoneNsCache is not null &&
                    zoneNsCache.TryGetValue(zoneKey, out (IReadOnlyList<IPAddress> Ips, DateTimeOffset ExpiresUtc) cached) &&
                    cached.ExpiresUtc > DateTimeOffset.UtcNow &&
                    cached.Ips.Count > 0)
                {
                    return (zoneKey, cached.Ips);
                }

                foreach (IPAddress resolver in RecursiveResolvers)
                {
                    byte[] query = DnsWireQueryBuilder.Build(cursor, DnsWireRecordTypes.Ns, out ushort queryId, recursionDesired: true);
                    byte[]? response = await SendUdpQueryAsync(resolver, query, cancellationToken).ConfigureAwait(false);
                    if (response is null || response.Length < DnsHeaderSize)
                    {
                        continue;
                    }

                    if (!TryParseNsResponse(response, queryId, out List<string> nsHostnames, out Dictionary<string, List<IPAddress>> glue))
                    {
                        continue;
                    }

                    if (nsHostnames.Count == 0)
                    {
                        break;
                    }

                    List<IPAddress> result = [];
                    foreach (string ns in nsHostnames)
                    {
                        if (glue.TryGetValue(NormalizeDnsName(ns), out List<IPAddress>? ips))
                        {
                            foreach (IPAddress ip in ips)
                            {
                                AddUnique(result, ip);
                            }
                        }
                        else
                        {
                            IReadOnlyList<IPAddress> resolved = await ResolveNsHostnameViaWireThenOsAsync(ns, cancellationToken).ConfigureAwait(false);
                            foreach (IPAddress ip in resolved)
                            {
                                AddUnique(result, ip);
                            }
                        }
                    }

                    if (result.Count > 0)
                    {
                        IReadOnlyList<IPAddress> ro = result;
                        if (zoneNsCache is not null && zoneCacheTtl > TimeSpan.Zero)
                        {
                            zoneNsCache[zoneKey] = (ro, DateTimeOffset.UtcNow + zoneCacheTtl);
                        }

                        return (zoneKey, ro);
                    }
                }

                int dot = cursor.IndexOf('.', StringComparison.Ordinal);
                cursor = cursor[(dot + 1)..];
            }

            return (string.Empty, Array.Empty<IPAddress>());
        }

        /// <summary>
        /// Adds a unique IP address to a list.
        /// </summary>
        /// <param name="list">The list to add the IP address to.</param>
        /// <param name="ip">The IP address to add.</param>
        private static void AddUnique(List<IPAddress> list, IPAddress ip)
        {
            foreach (IPAddress existing in list)
            {
                if (existing.Equals(ip))
                {
                    return;
                }
            }

            list.Add(ip);
        }

        private static string NormalizeDnsName(string name)
        {
            return name.TrimEnd('.').ToLowerInvariant();
        }

        /// <summary>
        /// Resolves an NS hostname: glue-equivalent via recursive wire A/AAAA, then OS stub resolver.
        /// </summary>
        /// <param name="host">The hostname to resolve.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The IP addresses of the resolved hostname; <see langword="null"/> if the hostname was not resolved.</returns>
        private static async Task<IReadOnlyList<IPAddress>> ResolveNsHostnameViaWireThenOsAsync(string host, CancellationToken cancellationToken)
        {
            List<IPAddress> wire = [];
            foreach (IPAddress resolver in RecursiveResolvers)
            {
                byte[] q4 = DnsWireQueryBuilder.Build(host, DnsWireRecordTypes.A, out ushort id4, recursionDesired: true);
                byte[]? r4 = await SendUdpQueryAsync(resolver, q4, cancellationToken).ConfigureAwait(false);
                if (r4 is not null)
                {
                    CollectIpv4AnswersFromResponse(r4, id4, wire);
                }

                byte[] q6 = DnsWireQueryBuilder.Build(host, DnsWireRecordTypes.Aaaa, out ushort id6, recursionDesired: true);
                byte[]? r6 = await SendUdpQueryAsync(resolver, q6, cancellationToken).ConfigureAwait(false);
                if (r6 is not null)
                {
                    CollectIpv6AnswersFromResponse(r6, id6, wire);
                }

                if (wire.Count > 0)
                {
                    return wire;
                }
            }

            return await ResolveHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Collects IPv4 answers from a DNS response.
        /// </summary>
        /// <param name="buffer">The full DNS response buffer.</param>
        /// <param name="expectedId">The expected ID of the response.</param>
        /// <param name="dest">The list to add the IP addresses to.</param>
        private static void CollectIpv4AnswersFromResponse(byte[] buffer, ushort expectedId, List<IPAddress> dest)
        {
            ReadOnlySpan<byte> span = buffer;
            if (span.Length < DnsHeaderSize)
            {
                return;
            }

            if (BinaryPrimitives.ReadUInt16BigEndian(span) != expectedId)
            {
                return;
            }

            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
            if ((flags & 0xFA0F) != 0x8000)
            {
                return;
            }

            ushort qdCount = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            ushort anCount = BinaryPrimitives.ReadUInt16BigEndian(span[6..]);
            int offset = DnsHeaderSize;
            if (!SkipQuestionSection(span, ref offset, qdCount))
            {
                return;
            }

            for (int i = 0; i < anCount; i++)
            {
                if (!TryConsumeResourceRecord(span, ref offset, out _, out ushort rrType, out ushort rrClass, out int rdataStart, out ushort rdLength))
                {
                    return;
                }

                if (rrClass != DnsWireQueryBuilder.DnsClassIn)
                {
                    continue;
                }

                if (rrType == DnsWireRecordTypes.A && rdLength == 4 && rdataStart + 4 <= span.Length)
                {
                    dest.Add(new IPAddress(span.Slice(rdataStart, 4)));
                }
            }
        }

        /// <summary>
        /// Collects IPv6 answers from a DNS response.
        /// </summary>
        /// <param name="buffer">The full DNS response buffer.</param>
        /// <param name="expectedId">The expected ID of the response.</param>
        /// <param name="dest">The list to add the IP addresses to.</param>
        private static void CollectIpv6AnswersFromResponse(byte[] buffer, ushort expectedId, List<IPAddress> dest)
        {
            ReadOnlySpan<byte> span = buffer;
            if (span.Length < DnsHeaderSize)
            {
                return;
            }

            if (BinaryPrimitives.ReadUInt16BigEndian(span) != expectedId)
            {
                return;
            }

            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
            if ((flags & 0xFA0F) != 0x8000)
            {
                return;
            }

            ushort qdCount = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            ushort anCount = BinaryPrimitives.ReadUInt16BigEndian(span[6..]);
            int offset = DnsHeaderSize;
            if (!SkipQuestionSection(span, ref offset, qdCount))
            {
                return;
            }

            for (int i = 0; i < anCount; i++)
            {
                if (!TryConsumeResourceRecord(span, ref offset, out _, out ushort rrType, out ushort rrClass, out int rdataStart, out ushort rdLength))
                {
                    return;
                }

                if (rrClass != DnsWireQueryBuilder.DnsClassIn)
                {
                    continue;
                }

                if (rrType == DnsWireRecordTypes.Aaaa && rdLength == 16 && rdataStart + 16 <= span.Length)
                {
                    dest.Add(new IPAddress(span.Slice(rdataStart, 16)));
                }
            }
        }

        /// <summary>
        /// Resolves a hostname via the OS stub resolver (last resort).
        /// </summary>
        /// <param name="host">The hostname to resolve.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The IP addresses of the resolved hostname; <see langword="null"/> if the hostname was not resolved.</returns>
        private static async Task<IReadOnlyList<IPAddress>> ResolveHostAddressesAsync(string host, CancellationToken ct)
        {
            try
            {
                IPAddress[] all = await System.Net.Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                if (all.Length == 0)
                {
                    return [];
                }

                List<IPAddress> ips = new(all.Length);
                foreach (IPAddress ip in all)
                {
                    if (ip.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    {
                        ips.Add(ip);
                    }
                }

                return ips;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return [];
            }
        }

        /// <summary>
        /// Sends a UDP query to a resolver and returns the response.
        /// </summary>
        /// <param name="resolver">The IP address of the resolver to send the query to.</param>
        /// <param name="query">The query to send.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The response from the resolver; <see langword="null"/> if the query was not successful.</returns>
        private static async Task<byte[]?> SendUdpQueryAsync(IPAddress resolver, byte[] query, CancellationToken ct)
        {
            using UdpClient udp = new(resolver.AddressFamily);
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(UdpTimeoutMs);

            try
            {
                _ = await udp.SendAsync(query, new IPEndPoint(resolver, 53), timeoutCts.Token).ConfigureAwait(false);
                UdpReceiveResult result = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
                return result.Buffer;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
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

        /// <summary>
        /// Tries to parse an NS response.
        /// </summary>
        /// <param name="buffer">The full DNS response buffer.</param>
        /// <param name="expectedId">The expected ID of the response.</param>
        /// <param name="nsHostnames">The list of NS hostnames.</param>
        /// <param name="glue">The dictionary of glue records.</param>
        /// <returns><see langword="true"/> if the response was successfully parsed; <see langword="false"/> if the buffer is
        /// too short, the hop limit is exceeded, or a reserved label type is encountered.</returns>
        private static bool TryParseNsResponse(
            byte[] buffer,
            ushort expectedId,
            out List<string> nsHostnames,
            out Dictionary<string, List<IPAddress>> glue)
        {
            nsHostnames = [];
            glue = new Dictionary<string, List<IPAddress>>(StringComparer.OrdinalIgnoreCase);
            ReadOnlySpan<byte> span = buffer;
            if (span.Length < DnsHeaderSize)
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt16BigEndian(span) != expectedId)
            {
                return false;
            }

            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
            if ((flags & 0xFA0F) != 0x8000)
            {
                return false;
            }

            ushort qdCount = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            ushort anCount = BinaryPrimitives.ReadUInt16BigEndian(span[6..]);
            ushort authorityCount = BinaryPrimitives.ReadUInt16BigEndian(span[8..]);
            ushort additionalCount = BinaryPrimitives.ReadUInt16BigEndian(span[10..]);

            int offset = DnsHeaderSize;
            if (!SkipQuestionSection(span, ref offset, qdCount))
            {
                return false;
            }

            ProcessSection(span, ref offset, anCount, nsHostnames, glue);
            ProcessSection(span, ref offset, authorityCount, nsHostnames, glue);
            ProcessSection(span, ref offset, additionalCount, nsHostnames, glue);

            return true;
        }

        /// <summary>
        /// Processes a section of a DNS response.
        /// </summary>
        /// <param name="span">The full DNS response buffer.</param>
        /// <param name="offset">Current read position; advanced past the section on return.</param>
        /// <param name="count">Number of resource records to process.</param>
        /// <param name="nsHostnames">The list of NS hostnames.</param>
        /// <param name="glue">The dictionary of glue records.</param>
        private static void ProcessSection(
            ReadOnlySpan<byte> span,
            ref int offset,
            int count,
            List<string> nsHostnames,
            Dictionary<string, List<IPAddress>> glue)
        {
            for (int i = 0; i < count; i++)
            {
                if (!TryConsumeResourceRecord(span, ref offset, out string owner, out ushort rrType, out ushort rrClass, out int rdataStart, out ushort rdLength))
                {
                    return;
                }

                if (rrClass != DnsWireQueryBuilder.DnsClassIn)
                {
                    continue;
                }

                if (rrType == DnsWireRecordTypes.Ns)
                {
                    int p = rdataStart;
                    if (!DnsWireNameReader.TryReadDomainName(span, ref p, out string nsdname) || p != rdataStart + rdLength)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(nsdname) && !nsHostnames.Contains(nsdname, StringComparer.OrdinalIgnoreCase))
                    {
                        nsHostnames.Add(nsdname);
                    }
                }
                else if (rrType == DnsWireRecordTypes.A && rdLength == 4)
                {
                    ReadOnlySpan<byte> rdata = span.Slice(rdataStart, rdLength);
                    AddGlue(glue, owner, new IPAddress(rdata));
                }
                else if (rrType == DnsWireRecordTypes.Aaaa && rdLength == 16)
                {
                    ReadOnlySpan<byte> rdata = span.Slice(rdataStart, rdLength);
                    AddGlue(glue, owner, new IPAddress(rdata));
                }
            }
        }

        /// <summary>
        /// Adds a glue record to the dictionary.
        /// </summary>
        /// <param name="glue">The dictionary to add the glue record to.</param>
        /// <param name="owner">The owner name of the glue record.</param>
        /// <param name="ip">The IP address of the glue record.</param>
        private static void AddGlue(Dictionary<string, List<IPAddress>> glue, string owner, IPAddress ip)
        {
            string key = NormalizeDnsName(owner);
            if (!glue.TryGetValue(key, out List<IPAddress>? list))
            {
                list = [];
                glue[key] = list;
            }

            foreach (IPAddress existing in list)
            {
                if (existing.Equals(ip))
                {
                    return;
                }
            }

            list.Add(ip);
        }

        /// <summary>
        /// Consumes a DNS resource record from a packet.
        /// </summary>
        /// <param name="packet">The full DNS response buffer.</param>
        /// <param name="offset">Current read position; advanced past the resource record on return.</param>
        /// <param name="ownerName">The owner name of the resource record.</param>
        /// <param name="rrType">The type of the resource record.</param>
        /// <param name="rrClass">The class of the resource record.</param>
        /// <param name="rdataStart">The start position of the resource record data.</param>
        /// <param name="rdLength">The length of the resource record data.</param>
        /// <returns><see langword="true"/> if the resource record was successfully consumed; <see langword="false"/> if the buffer is
        /// too short, the hop limit is exceeded, or a reserved label type is encountered.</returns>
        private static bool TryConsumeResourceRecord(
            ReadOnlySpan<byte> packet,
            ref int offset,
            out string ownerName,
            out ushort rrType,
            out ushort rrClass,
            out int rdataStart,
            out ushort rdLength)
        {
            rrType = 0;
            rrClass = 0;
            rdataStart = 0;
            rdLength = 0;

            if (!DnsWireNameReader.TryReadDomainName(packet, ref offset, out ownerName))
            {
                return false;
            }

            if (offset + 10 > packet.Length)
            {
                return false;
            }

            rrType = BinaryPrimitives.ReadUInt16BigEndian(packet[offset..]);
            rrClass = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 2)..]);
            rdLength = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 8)..]);
            offset += 10;
            rdataStart = offset;
            if (offset + rdLength > packet.Length)
            {
                return false;
            }

            offset += rdLength;
            return true;
        }

        /// <summary>
        /// Advances <paramref name="offset"/> past the question section of a DNS response.
        /// </summary>
        /// <param name="span">The full DNS response buffer.</param>
        /// <param name="offset">Current read position; advanced past the question section on return.</param>
        /// <param name="qdCount">Number of questions to skip.</param>
        /// <returns><see langword="true"/> if the question section was successfully skipped; <see langword="false"/> if the buffer is
        /// too short, the hop limit is exceeded, or a reserved label type is encountered.</returns>
        private static bool SkipQuestionSection(ReadOnlySpan<byte> span, ref int offset, ushort qdCount)
        {
            for (int q = 0; q < qdCount; q++)
            {
                if (!DnsWireNameSkipper.TrySkipName(span, ref offset))
                {
                    return false;
                }

                if (offset + QuestionSuffixSize > span.Length)
                {
                    return false;
                }

                offset += QuestionSuffixSize;
            }

            return true;
        }
    }
}
