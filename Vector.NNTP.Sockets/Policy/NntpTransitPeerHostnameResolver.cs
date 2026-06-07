// <copyright file="NntpTransitPeerHostnameResolver.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: peer FQDN resolution for truthful SpamAssassin Received synthesis at article enqueue.

using Vector.NNTP.Sockets.Session;

namespace Vector.NNTP.Sockets.Policy
{
    /// <summary>
    /// Resolves a public peer hostname for transit article origin metadata used when synthesizing spamd scan headers.
    /// </summary>
    /// <remarks>
    /// <para><b>Resolution order:</b></para>
    /// <list type="number">
    /// <item><description>Use <see cref="NntpConnectionContext.TransitPeerMatchedEntry"/> when it is a hostname AcceptFrom entry.</description></item>
    /// <item><description>Attempt reverse DNS on <see cref="NntpConnectionContext.ClientRemoteEndPoint"/>.</description></item>
    /// <item><description>Return <see langword="null"/> when only the peer IP should be used (no forged hostname).</description></item>
    /// </list>
    /// </remarks>
    public static class NntpTransitPeerHostnameResolver
    {
        /// <summary>
        /// Maximum time to wait for reverse DNS during article enqueue.
        /// </summary>
        private static readonly TimeSpan ReverseDnsTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Resolves the peer hostname for a transit connection at article enqueue time.
        /// </summary>
        /// <param name="connection">Active NNTP connection context.</param>
        /// <param name="cancellationToken">Cancellation token linked with a bounded reverse-DNS timeout.</param>
        /// <returns>Public FQDN when resolved; otherwise <see langword="null"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connection"/> is <see langword="null"/>.</exception>
        public static async ValueTask<string?> ResolveAsync(
            NntpConnectionContext connection,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);

            if (IsAcceptableHostnameEntry(connection.TransitPeerMatchedEntry))
            {
                return connection.TransitPeerMatchedEntry!.Trim();
            }

            return await TryReverseDnsAsync(connection.ClientRemoteEndPoint.Address, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Determines whether a transit peer AcceptFrom configuration entry is a hostname rather than IP/CIDR.
        /// </summary>
        /// <param name="entry">Matched AcceptFrom entry text.</param>
        /// <returns><see langword="true"/> when <paramref name="entry"/> should be used as a peer hostname.</returns>
        public static bool IsAcceptableHostnameEntry(string? entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return false;
            }

            string trimmed = entry.Trim();
            if (LooksLikeIpOrCidr(trimmed))
            {
                return false;
            }

            return trimmed.Contains('.', StringComparison.Ordinal);
        }

        /// <summary>
        /// Attempts reverse DNS and accepts only public routable FQDN results.
        /// </summary>
        /// <param name="address">Peer IP address.</param>
        /// <param name="cancellationToken">Caller cancellation token.</param>
        /// <returns>Resolved hostname or <see langword="null"/>.</returns>
        private static async ValueTask<string?> TryReverseDnsAsync(IPAddress address, CancellationToken cancellationToken)
        {
            if (IPAddress.IsLoopback(address) || IsPrivateOrNonRoutable(address))
            {
                return null;
            }

            try
            {
                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(ReverseDnsTimeout);
                IPHostEntry entry = await Dns.GetHostEntryAsync(address.ToString(), timeoutCts.Token).ConfigureAwait(false);
                string? hostName = entry.HostName;
                if (!IsAcceptableReverseDnsName(hostName, address))
                {
                    return null;
                }

                return hostName!.TrimEnd('.');
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (SocketException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns whether <paramref name="text"/> looks like a literal IP address or CIDR range.
        /// </summary>
        /// <param name="text">AcceptFrom entry text.</param>
        /// <returns><see langword="true"/> for IP or CIDR literals.</returns>
        private static bool LooksLikeIpOrCidr(string text)
        {
            if (text.Contains('/', StringComparison.Ordinal))
            {
                return true;
            }

            return IPAddress.TryParse(text, out _);
        }

        /// <summary>
        /// Returns whether <paramref name="address"/> is private, link-local, or otherwise non-routable.
        /// </summary>
        /// <param name="address">Candidate peer address.</param>
        /// <returns><see langword="true"/> when reverse DNS should not be attempted.</returns>
        private static bool IsPrivateOrNonRoutable(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
            }

            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
            {
                return true;
            }

            return bytes[0] switch
            {
                10 => true,
                127 => true,
                0 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                _ => false,
            };
        }

        /// <summary>
        /// Validates a reverse-DNS hostname as a public FQDN suitable for mail-style <c>Received:</c> headers.
        /// </summary>
        /// <param name="hostName">PTR result hostname.</param>
        /// <param name="address">Peer address used for the lookup.</param>
        /// <returns><see langword="true"/> when the hostname is acceptable.</returns>
        private static bool IsAcceptableReverseDnsName(string? hostName, IPAddress address)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                return false;
            }

            string trimmed = hostName.Trim().TrimEnd('.');
            if (!trimmed.Contains('.', StringComparison.Ordinal))
            {
                return false;
            }

            if (trimmed.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("in-addr.arpa", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("ip6.arpa", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IPAddress.TryParse(trimmed, out IPAddress? asIp) && asIp.Equals(address))
            {
                return false;
            }

            return true;
        }
    }
}
