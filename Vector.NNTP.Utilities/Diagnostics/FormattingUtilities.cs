// <copyright file="FormattingUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: diagnostics/formatting/startup validation; readability over micro-optimization; allocations acceptable.
// FormattingUtilities.cs -- Allocation-conscious formatting helpers for human-readable log output.
//
// Allocation characteristics:
//   - Cold-path helpers allocate StringBuilder and result strings for diagnostic output.
//
// Thread safety:
//   All methods are static and stateless. Safe for concurrent use from any thread.

using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Vector.NNTP.Utilities.Diagnostics
{
    /// <summary>
    /// Allocation-conscious formatting helpers for human-readable log output.
    /// </summary>
    /// <remarks>
    /// <para><b>Design goal:</b> Provide small, dependency-free formatting helpers that are safe to call from logging paths
    /// without causing large allocations or leaking sensitive values.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless.</para>
    ///
    /// <para><b>Performance:</b> COLD PATH — <see cref="StringBuilder"/> and formatted strings allocate; safe for
    /// diagnostic logging only.</para>
    /// </remarks>
    public static class FormattingUtilities
    {
        /// <summary>
        /// Number of bytes per gibibyte (GiB) as a <see cref="double"/>.
        /// </summary>
        public const double BytesPerGiB = 1_024.0 * 1_024 * 1_024;

        /// <summary>
        /// Default maximum byte length used by <see cref="FormatObjectValue"/> when the caller does not specify a cap.
        /// </summary>
        public const int DefaultMaxByteLength = 256;

        /// <summary>
        /// Converts a byte count to gibibytes for human-readable log output.
        /// </summary>
        /// <param name="bytes">Byte count.</param>
        /// <returns>GiB value as a <see cref="double"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToGiB(long bytes)
        {
            return bytes / BytesPerGiB;
        }

        /// <summary>
        /// Normalises an <see cref="IPAddress"/> by converting IPv4-mapped IPv6 addresses (<c>::ffff:a.b.c.d</c>) to their
        /// IPv4 form.
        /// </summary>
        /// <param name="address">Address to normalise.</param>
        /// <returns>The normalised address.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="address"/> is <see langword="null"/>.</exception>
        public static IPAddress NormaliseAddress(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }

        /// <summary>
        /// Formats an IP address and port for human-readable endpoint display (RFC 3986 host:port syntax).
        /// </summary>
        /// <param name="address">IP address (IPv4-mapped IPv6 addresses are normalised to IPv4).</param>
        /// <param name="port">TCP or UDP port number.</param>
        /// <returns>
        /// IPv4: <c>a.b.c.d:port</c>; IPv6: <c>[addr]:port</c> with brackets around the address literal only.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="address"/> is <see langword="null"/>.</exception>
        public static string FormatHostPort(IPAddress address, int port)
        {
            ArgumentNullException.ThrowIfNull(address);
            IPAddress normalised = NormaliseAddress(address);
            if (normalised.AddressFamily == AddressFamily.InterNetwork)
            {
                return $"{normalised}:{port}";
            }

            return $"[{normalised}]:{port}";
        }

        /// <summary>
        /// Formats an <see cref="IPEndPoint"/> for human-readable endpoint display.
        /// </summary>
        /// <param name="endPoint">Socket endpoint.</param>
        /// <returns>Formatted <c>host:port</c> string per <see cref="FormatHostPort(IPAddress, int)"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endPoint"/> is <see langword="null"/>.</exception>
        public static string FormatIPEndPoint(IPEndPoint endPoint)
        {
            ArgumentNullException.ThrowIfNull(endPoint);
            return FormatHostPort(endPoint.Address, endPoint.Port);
        }

        /// <summary>
        /// Formats a connection-scoped log prefix bracketing the client endpoint for RX/TX correlation.
        /// </summary>
        /// <param name="endPoint">Effective client endpoint (post-PROXY).</param>
        /// <returns>
        /// IPv4: <c>[a.b.c.d:port]</c>; IPv6: <c>[addr]:port</c> without double-bracketing the address.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endPoint"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// IPv6 uses RFC 3986 <c>[addr]:port</c> form directly as the log prefix; IPv4 wraps <c>addr:port</c> in brackets.
        /// </remarks>
        public static string FormatConnectionLogPrefix(IPEndPoint endPoint)
        {
            ArgumentNullException.ThrowIfNull(endPoint);
            IPAddress normalised = NormaliseAddress(endPoint.Address);
            if (normalised.AddressFamily == AddressFamily.InterNetwork)
            {
                return $"[{normalised}:{endPoint.Port}]";
            }

            return $"[{normalised}]:{endPoint.Port}";
        }

        /// <summary>
        /// Formats a host array and port into a human-readable summary string.
        /// </summary>
        /// <param name="hosts">Hostnames or IP literals.</param>
        /// <param name="port">Port number.</param>
        /// <returns>A string such as <c>"host1:5672, host2:5672"</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="hosts"/> is <see langword="null"/>.</exception>
        public static string FormatEndpointSummary(string[] hosts, int port)
        {
            ArgumentNullException.ThrowIfNull(hosts);

            if (hosts.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new(capacity: hosts.Length * 24);

            for (int i = 0; i < hosts.Length; i++)
            {
                if (i != 0)
                {
                    _ = sb.Append(", ");
                }

                _ = sb.Append(hosts[i]);
                _ = sb.Append(':');
                _ = sb.Append(port);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats key-value pairs into <c>"k=v, k2=v2"</c> text for diagnostic logging.
        /// </summary>
        /// <param name="pairs">Pairs to format.</param>
        /// <returns>Formatted string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pairs"/> is <see langword="null"/>.</exception>
        public static string FormatKeyValuePairs(IReadOnlyDictionary<string, object> pairs)
        {
            ArgumentNullException.ThrowIfNull(pairs);
            return FormatKeyValuePairsCore(pairs);
        }

        /// <summary>
        /// Formats key-value pairs into <c>"k=v, k2=v2"</c> text for diagnostic logging.
        /// </summary>
        /// <param name="pairs">Pairs to format.</param>
        /// <returns>Formatted string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pairs"/> is <see langword="null"/>.</exception>
        public static string FormatKeyValuePairs(IDictionary<string, object> pairs)
        {
            ArgumentNullException.ThrowIfNull(pairs);
            return FormatKeyValuePairsCore(pairs);
        }

        /// <summary>
        /// Formats an arbitrary object value for diagnostic logging.
        /// </summary>
        /// <param name="value">Value to format.</param>
        /// <param name="maxByteLength">Maximum byte length when decoding <see cref="byte"/> arrays as UTF-8.</param>
        /// <returns>A safe string representation.</returns>
        public static string FormatObjectValue(object? value, int maxByteLength = DefaultMaxByteLength)
        {
            return value is null ? "<null>" : value is byte[] bytes ? FormatByteArray(bytes, maxByteLength) : value.ToString() ?? "<null>";
        }

        /// <summary>
        /// Formats key/value pairs into a single diagnostic string, omitting entries whose formatted value is empty.
        /// </summary>
        /// <param name="pairs">The pairs to format.</param>
        /// <returns>A comma-separated <c>key=value</c> string.</returns>
        private static string FormatKeyValuePairsCore(IEnumerable<KeyValuePair<string, object>> pairs)
        {
            StringBuilder sb = new(capacity: 64);
            bool first = true;
            bool wroteAny = false;

            foreach (KeyValuePair<string, object> kvp in pairs)
            {
                if (!first)
                {
                    _ = sb.Append(", ");
                }

                first = false;
                wroteAny = true;
                _ = sb.Append(kvp.Key);
                _ = sb.Append('=');
                _ = sb.Append(FormatObjectValue(kvp.Value, DefaultMaxByteLength));
            }

            return wroteAny ? sb.ToString() : string.Empty;
        }

        /// <summary>
        /// Formats a UTF-8 byte array for display with truncation.
        /// </summary>
        /// <param name="bytes">UTF-8 bytes.</param>
        /// <param name="maxByteLength">Maximum bytes to decode.</param>
        /// <returns>A quoted string when decoding succeeds; otherwise a length placeholder.</returns>
        private static string FormatByteArray(byte[] bytes, int maxByteLength)
        {
            if (bytes.Length == 0)
            {
                return "\"\"";
            }

            int length = Math.Min(bytes.Length, maxByteLength);

            try
            {
                string s = System.Text.Encoding.UTF8.GetString(bytes, 0, length);
                return bytes.Length > length ? $"\"{s}...\"" : $"\"{s}\"";
            }
            catch (Exception)
            {
                return $"<{bytes.Length} bytes>";
            }
        }
    }
}
