// <copyright file="IPUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// IPUtilities.cs -- Classifies IPv4 addresses into private and reserved ranges.

using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.Utilities.Networking
{
    /// <summary>
    /// Classifies IPv4 addresses into well-known private and reserved ranges for configuration validation.
    /// </summary>
    public static class IPUtilities
    {
        /// <summary>
        /// Minimum address prefix length in bytes required for private/reserved range classification.
        /// </summary>
        private const int MinBytesRequired = 2;

        /// <summary>
        /// Classifies an IPv4 address prefix by its first bytes.
        /// </summary>
        /// <param name="bytes">IPv4 address bytes in network byte order (must contain at least 2 bytes).</param>
        /// <returns>A description string, or <see langword="null"/> when not private/reserved.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string? Classify(ReadOnlySpan<byte> bytes)
        {
            return bytes.Length < MinBytesRequired
                ? null
                : bytes[0] switch
                {
                    10 => "RFC 1918 private (10.0.0.0/8)",
                    127 => "RFC 1122 loopback (127.0.0.0/8)",
                    172 when bytes[1] is >= 16 and <= 31 => "RFC 1918 private (172.16.0.0/12)",
                    192 when bytes[1] == 168 => "RFC 1918 private (192.168.0.0/16)",
                    100 when bytes[1] is >= 64 and <= 127 => "RFC 6598 CGN shared (100.64.0.0/10)",
                    169 when bytes[1] == 254 => "RFC 3927 link-local (169.254.0.0/16)",
                    _ => null,
                };
        }

        /// <summary>
        /// Classifies an <see cref="IPAddress"/> as belonging to a well-known private or reserved IPv4 range.
        /// </summary>
        /// <param name="address">IP address.</param>
        /// <returns>A description string, or <see langword="null"/> when not private/reserved.</returns>
        public static string? Classify(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            IPAddress normalised = FormattingUtilities.NormaliseAddress(address);
            if (normalised.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            Span<byte> bytes = stackalloc byte[4];
            return !normalised.TryWriteBytes(bytes, out _) ? null : Classify(bytes);
        }

        /// <summary>
        /// Returns <see langword="true"/> when the address matches any private/reserved range detected by this utility.
        /// </summary>
        /// <param name="address">IP address.</param>
        /// <returns><see langword="true"/> for private/reserved; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPrivateOrReserved(IPAddress address)
        {
            return Classify(address) is not null;
        }
    }
}
