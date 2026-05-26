// IPUtilities.cs -- Classifies IPv4 addresses into well-known private and reserved ranges.
//
// Provides reusable methods for detecting RFC 1918, RFC 6598, RFC 3927, and RFC 1122 address ranges.
// Extracted from RabbitMQOptions.ClassifyPrivateIPv4Range to eliminate duplication and ensure all
// configuration validators use the same range classification logic.
//
// All methods are static and thread-safe.  No shared mutable state exists.
//
// Cross-platform:
//   Fully portable.  Uses only ReadOnlySpan<byte> pattern matching on raw IPv4 address bytes.  No
//   P/Invoke, no OS-specific APIs.  Address byte order is network byte order (big-endian) as produced
//   by IPAddress.TryWriteBytes on all .NET 8 runtimes (Windows x64, Linux x64).
//
//   IPv4-mapped IPv6 addresses (::ffff:a.b.c.d) are normalised via FormattingUtilities.NormaliseAddress
//   before classification, ensuring correct behaviour on dual-stack sockets (the Linux default).
//
// SIMD applicability:
//   Not applicable.  Classification is a single switch expression on at most two bytes -- no contiguous
//   memory buffers, byte-level pattern searches, or bulk numeric operations that would benefit from
//   vector instructions.
//
// Security:
//   No credentials, connection strings, or PII are stored or logged.  All inputs and outputs are IP
//   address bytes and string descriptions -- non-sensitive operational data.
//
//   Input validation: The Classify(ReadOnlySpan<byte>) overload validates that the span contains at
//   least 2 bytes before accessing indexed elements, preventing IndexOutOfRangeException from malformed
//   input.  The Classify(IPAddress) overload validates against null and rejects non-IPv4 addresses.
//
// Consumers:
//   RabbitMQOptions  -- production safety checks on Hosts[] entries.
//   (Future)         -- any options class needing IPv4 private range detection.

using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace MessageBus.Utilities
{
    /// <summary>
    /// Classifies IPv4 addresses into well-known private and reserved ranges for configuration validation.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Centralises IPv4 range classification so all configuration validators (e.g.,
    /// <see cref="Configuration.RabbitMQOptions"/>) use the same logic for detecting RFC 1918, RFC 6598,
    /// RFC 3927, and RFC 1122 address ranges.  Prevents divergence when new options classes add
    /// production-safety checks.</para>
    ///
    /// <para><b>Ranges detected:</b></para>
    /// <list type="bullet">
    ///   <item><c>10.0.0.0/8</c> -- RFC 1918 Class A private.</item>
    ///   <item><c>172.16.0.0/12</c> -- RFC 1918 Class B private.</item>
    ///   <item><c>192.168.0.0/16</c> -- RFC 1918 Class C private.</item>
    ///   <item><c>100.64.0.0/10</c> -- RFC 6598 Carrier-Grade NAT (CGNAT).</item>
    ///   <item><c>169.254.0.0/16</c> -- RFC 3927 link-local (APIPA).</item>
    ///   <item><c>127.0.0.0/8</c> -- RFC 1122 loopback.</item>
    /// </list>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless.  Safe for concurrent use
    /// from any number of threads without synchronisation.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  Uses only <see cref="ReadOnlySpan{T}"/> pattern matching on raw
    /// IPv4 address bytes produced by <see cref="IPAddress.TryWriteBytes"/>.  Byte order is network byte order
    /// (big-endian) on all .NET 8 runtimes.  IPv4-mapped IPv6 addresses (<c>::ffff:a.b.c.d</c>) are normalised via
    /// <see cref="FormattingUtilities.NormaliseAddress"/> before classification, ensuring correct behaviour on
    /// dual-stack sockets (the Linux default).</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  Classification is a single switch expression on at most
    /// two bytes -- no vectorisable computation paths.</para>
    ///
    /// <para><b>Security:</b> No credentials, connection strings, or PII are stored or logged.  All inputs and
    /// outputs are IP address bytes and string descriptions -- non-sensitive operational data.</para>
    /// </remarks>
    internal static class IPUtilities
    {

        #region Constants

        /// <summary>
        /// Minimum number of bytes required for classification.  The switch expression inspects at most
        /// <c>bytes[0]</c> and <c>bytes[1]</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> 2 bytes.  Although a full IPv4 address is 4 bytes, classification only
        /// needs the first two octets.  Requiring exactly 2 avoids rejecting callers who pass a 2-byte
        /// prefix for quick lookups while still preventing <see cref="IndexOutOfRangeException"/> from
        /// short or empty spans.</para>
        /// </remarks>
        private const int MinBytesRequired = 2;

        #endregion

        #region Public Methods

        /// <summary>
        /// Classifies an IPv4 address as belonging to a well-known private or reserved range.
        /// </summary>
        /// <remarks>
        /// <para><b>Ranges detected:</b></para>
        /// <list type="bullet">
        ///   <item><c>10.0.0.0/8</c> -- RFC 1918 Class A private.</item>
        ///   <item><c>172.16.0.0/12</c> -- RFC 1918 Class B private.</item>
        ///   <item><c>192.168.0.0/16</c> -- RFC 1918 Class C private.</item>
        ///   <item><c>100.64.0.0/10</c> -- RFC 6598 Carrier-Grade NAT (CGNAT).</item>
        ///   <item><c>169.254.0.0/16</c> -- RFC 3927 link-local (APIPA).</item>
        ///   <item><c>127.0.0.0/8</c> -- RFC 1122 loopback.</item>
        /// </list>
        ///
        /// <para><b>Input validation:</b> Returns <see langword="null"/> if <paramref name="bytes"/> contains
        /// fewer than <see cref="MinBytesRequired"/> bytes.  This prevents <see cref="IndexOutOfRangeException"/>
        /// from malformed or truncated input without imposing the cost of a full 4-byte validation on every
        /// call.</para>
        ///
        /// <para><b>Performance:</b> Marked with <see cref="MethodImplAttribute"/>
        /// (<see cref="MethodImplOptions.AggressiveInlining"/>) because the method body is a single bounds check
        /// plus a switch expression -- inlining eliminates call overhead and allows the JIT to fold the bounds
        /// check into the caller's control flow.</para>
        /// </remarks>
        /// <param name="bytes">The IPv4 address bytes in network byte order (big-endian).  Must contain at least
        /// <see cref="MinBytesRequired"/> bytes.  Accepted as <see cref="ReadOnlySpan{T}"/> to support both
        /// <c>stackalloc</c> and heap-allocated callers.</param>
        /// <returns>A description of the matching range, or <see langword="null"/> if the span is too short or the
        /// address is not private/reserved.</returns>
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
                    _ => null
                };
        }

        /// <summary>
        /// Classifies an <see cref="IPAddress"/> as belonging to a well-known private or reserved IPv4 range.
        /// Returns <see langword="null"/> for IPv6 addresses and non-private IPv4 addresses.
        /// </summary>
        /// <param name="address">The IP address to classify.  Must not be <see langword="null"/>.</param>
        /// <returns>A description of the matching range, or <see langword="null"/> if the address is IPv6 or not
        /// private/reserved.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="address"/> is
        /// <see langword="null"/>.</exception>
        /// <remarks>
        /// <para><b>IPv4-mapped IPv6 normalisation:</b> Addresses in IPv4-mapped IPv6 form
        /// (<c>::ffff:a.b.c.d</c>) are normalised to their IPv4 representation via
        /// <see cref="FormattingUtilities.NormaliseAddress"/> before classification.  This ensures correct
        /// behaviour on dual-stack sockets where accepted connections present IPv4 addresses in mapped form --
        /// the default on Linux and when <see cref="Socket.DualMode"/> is enabled on
        /// Windows.</para>
        ///
        /// <para><b>IPv6 short-circuit:</b> Returns <see langword="null"/> immediately for non-IPv4 addresses
        /// after normalisation.  Only <see cref="AddressFamily.InterNetwork"/> addresses are classified.</para>
        ///
        /// <para><b>Allocation:</b> Uses <c>stackalloc</c> for the 4-byte buffer passed to
        /// <see cref="IPAddress.TryWriteBytes"/>, avoiding the <c>byte[]</c> allocation from
        /// <see cref="IPAddress.GetAddressBytes"/>.</para>
        /// </remarks>
        public static string? Classify(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            // Normalise IPv4-mapped IPv6 addresses (::ffff:a.b.c.d) to their IPv4 form so that dual-stack
            // socket addresses are classified correctly on both Windows and Linux.
            IPAddress normalised = FormattingUtilities.NormaliseAddress(address);
            if (normalised.AddressFamily != AddressFamily.InterNetwork)
                return null;
            Span<byte> bytes = stackalloc byte[4];
            return !normalised.TryWriteBytes(bytes, out _) ? null : Classify(bytes);
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="address"/> is in a well-known private or reserved IPv4
        /// range; <see langword="false"/> otherwise.
        /// </summary>
        /// <param name="address">The IP address to check.  Must not be <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if the address matches any detected range; <see langword="false"/> for
        /// IPv6 addresses, non-private IPv4 addresses, and addresses that cannot be classified.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="address"/> is
        /// <see langword="null"/>.</exception>
        /// <remarks>
        /// <para><b>Convenience wrapper:</b> Equivalent to <c>Classify(address) is not null</c>.  Provided for
        /// callers that only need a boolean result without the range description string (e.g., guard clauses,
        /// conditional branching).</para>
        ///
        /// <para><b>IPv4-mapped IPv6 normalisation:</b> Delegates to <see cref="Classify(IPAddress)"/>, which
        /// normalises IPv4-mapped IPv6 addresses before classification.  See
        /// <see cref="Classify(IPAddress)"/> remarks for details.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPrivateOrReserved(IPAddress address)
        {
            return Classify(address) is not null;
        }

        #endregion

    }
}
