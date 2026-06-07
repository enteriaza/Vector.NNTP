// <copyright file="HostParsingUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: no LINQ; no allocations on success; no closures; no boxing; prefer Span/stackalloc; throws extracted + NoInlining where documented.
// HostParsingUtilities.cs -- Shared host string parsing helpers for configuration validation.
//
// Thread safety:
//   All methods are static and stateless. Safe for concurrent use from any thread.

using System.Net;
using System.Runtime.CompilerServices;

namespace Vector.NNTP.Utilities.Networking
{
    /// <summary>
    /// Host string parsing helpers for configuration validation: port suffix detection, URI scheme detection, and IPv6
    /// bracket stripping.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless.</para>
    /// </remarks>
    public static class HostParsingUtilities
    {
        /// <summary>
        /// Returns <see langword="true"/> when a host string appears to contain a <c>":port"</c> suffix.
        /// </summary>
        /// <param name="host">Host string to inspect.</param>
        /// <returns><see langword="true"/> if the host appears to contain a port suffix.</returns>
        /// <remarks>Delegates to the span overload after null/empty checks.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasPortSuffix(string? host)
        {
            return !string.IsNullOrEmpty(host) && HasPortSuffix(host.AsSpan());
        }

        /// <summary>
        /// Returns <see langword="true"/> when a host span appears to contain a <c>":port"</c> suffix.
        /// </summary>
        /// <param name="host">Host span to inspect.</param>
        /// <returns><see langword="true"/> if the host appears to contain a port suffix.</returns>
        /// <remarks>
        /// Literal IP addresses parsed by <see cref="IPAddress.TryParse(ReadOnlySpan{char}, out IPAddress?)"/> return
        /// <see langword="false"/> even when they contain colons (IPv6). For hostnames, the last colon must be followed
        /// only by decimal digits to be treated as a port suffix.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasPortSuffix(ReadOnlySpan<char> host)
        {
            if (host.IsEmpty)
            {
                return false;
            }

            if (IPAddress.TryParse(host, out _))
            {
                return false;
            }

            int lastColon = host.LastIndexOf(':');
            if (lastColon < 0 || lastColon == host.Length - 1)
            {
                return false;
            }

            ReadOnlySpan<char> afterColon = host[(lastColon + 1)..];
            foreach (char c in afterColon)
            {
                if (c is < '0' or > '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns <see langword="true"/> when a host string contains a URI scheme separator (<c>"://"</c>).
        /// </summary>
        /// <param name="host">Host string to inspect.</param>
        /// <returns><see langword="true"/> if a scheme separator is present.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasUriScheme(string? host)
        {
            return !string.IsNullOrEmpty(host) && host.Contains("://", StringComparison.Ordinal);
        }

        /// <summary>
        /// Strips RFC 3986 IPv6 literal brackets (<c>"[::1]"</c> -&gt; <c>"::1"</c>). Returns the input unchanged for
        /// non-bracketed inputs.
        /// </summary>
        /// <param name="host">Host string.</param>
        /// <returns>Unwrapped host string, or <see langword="null"/> if input is <see langword="null"/>.</returns>
        public static string? StripIPv6Brackets(string? host)
        {
            return host is null ? null : StripIPv6Brackets(host.AsSpan());
        }

        /// <summary>
        /// Strips RFC 3986 IPv6 literal brackets from a host span.
        /// </summary>
        /// <param name="host">Host span.</param>
        /// <returns>Unwrapped host string.</returns>
        public static string StripIPv6Brackets(ReadOnlySpan<char> host)
        {
            return host.Length >= 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1].ToString() : host.ToString();
        }
    }
}
