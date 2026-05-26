// HostParsingUtilities.cs -- Shared host string parsing helpers for configuration validation.
//
// Provides reusable methods for detecting port suffixes, URI scheme prefixes, and stripping IPv6
// bracket notation from host strings.  Extracted from RabbitMQOptions to centralise host-format
// detection logic so all configuration validators use the same implementation.
//
// All methods are static and thread-safe.  No shared mutable state exists.
//
// Cross-platform:
//   Fully portable.  Uses only BCL string operations and IPAddress.TryParse, available on all .NET 8
//   runtimes (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs.
//
// SIMD applicability:
//   Not applicable.  This class performs scalar string inspection (LastIndexOf, character checks,
//   IPAddress.TryParse).  There are no contiguous memory buffers or bulk data operations that would
//   benefit from vector instructions.
//
// Consumers:
//   RabbitMQOptions  -- validates Hosts[] entries do not contain port suffixes or URI schemes,
//                       strips bracket notation from IPv6 literals.
//   RadiusOptions    -- validates RadiusServers[] entries do not contain port suffixes or URI schemes,
//                       strips bracket notation from IPv6 literals.
//   (Future)         -- any options class needing host:port detection or URI scheme rejection.

using System.Net;
using System.Runtime.CompilerServices;

namespace MessageBus.Utilities
{
    /// <summary>
    /// Shared host string parsing helpers for configuration validation.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Centralises host-format detection (port suffix, URI scheme prefix, IPv6 bracket notation) so
    /// all configuration validators use the same implementation.  Prevents divergence when new options classes add
    /// host-format validation.</para>
    ///
    /// <para><b>Consumers:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="Configuration.RabbitMQOptions"/> -- validates <c>Hosts[]</c> entries do not contain
    ///     port suffixes or URI schemes, strips bracket notation from IPv6 literals.</description></item>
    /// </list>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless.  Safe for concurrent use
    /// from any number of threads without synchronisation.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  Uses only BCL string operations and
    /// <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>, available on all .NET 8 runtimes (Windows x64, Linux x64).</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  Scalar string inspection -- no vectorisable computation
    /// paths.</para>
    /// </remarks>
    internal static class HostParsingUtilities
    {

        #region Public Methods

        /// <summary>
        /// Detects whether a host string contains a port suffix (<c>":digits"</c> at the end) that should be specified via
        /// a dedicated <c>Port</c> property instead.
        /// </summary>
        /// <remarks>
        /// <para><b>Strategy:</b> Uses <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> as the primary discriminator.  If the string parses
        /// as a valid IP address (v4 or v6), it cannot contain a port suffix -- <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> rejects
        /// port-suffixed strings like <c>"198.18.0.70:5672"</c>.  This eliminates the need for hand-rolled
        /// multi-colon/bracket heuristics and correctly handles all IPv6 forms including ambiguous cases like
        /// <c>"fe80::1:5672"</c> (a valid IPv6 address where <c>5672</c> is a hex segment, not a port).</para>
        ///
        /// <para>For strings that do <em>not</em> parse as an IP address (hostnames like <c>"rabbit.local:5672"</c>, or
        /// bracket-wrapped IPv6 with port like <c>"[::1]:5672"</c>), a simple last-colon + all-digits check detects port
        /// suffixes.</para>
        ///
        /// <para><b>Port number overflow:</b> This method does <em>not</em> validate that the numeric suffix represents a
        /// valid port range (1--65535).  Its purpose is format detection, not value validation.  A host string like
        /// <c>"rabbit.local:99999"</c> returns <see langword="true"/> because it structurally contains a port suffix,
        /// even though the value is out of range.</para>
        ///
        /// <para><b>Examples:</b></para>
        /// <list type="table">
        ///   <listheader>
        ///     <term>Input</term>
        ///     <description>Result</description>
        ///   </listheader>
        ///   <item><term><c>"rabbit.local:5672"</c></term>
        ///     <description><c>true</c> -- hostname with port.</description></item>
        ///   <item><term><c>"2001:db8::1"</c></term>
        ///     <description><c>false</c> -- valid IPv6.</description></item>
        ///   <item><term><c>"fe80::1:5672"</c></term>
        ///     <description><c>false</c> -- valid IPv6 (5672 is hex).</description></item>
        ///   <item><term><c>"[::1]:5672"</c></term>
        ///     <description><c>true</c> -- bracket-wrapped IPv6 with port.</description></item>
        ///   <item><term><c>"198.18.0.70:5672"</c></term>
        ///     <description><c>true</c> -- IPv4 with port.</description></item>
        ///   <item><term><c>""</c> or <see langword="null"/></term>
        ///     <description><c>false</c> -- empty/null input cannot contain a port.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="host">The host string to inspect.  <see langword="null"/> and empty strings return
        /// <see langword="false"/>.</param>
        /// <returns><see langword="true"/> if the host appears to contain a <c>":port"</c> suffix.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasPortSuffix(string? host)
        {
            if (string.IsNullOrEmpty(host))
                return false;
            // Valid IP addresses (v4 or v6) never contain a port suffix -- IPAddress.TryParse rejects
            // port-suffixed strings like "198.18.0.70:5672".  This eliminates ambiguity with IPv6 colons.
            if (IPAddress.TryParse(host, out _))
                return false;
            int lastColon = host.LastIndexOf(':');
            if (lastColon < 0 || lastColon == host.Length - 1)
                return false;
            // Verify every character after the last colon is an ASCII digit.
            ReadOnlySpan<char> afterColon = host.AsSpan(lastColon + 1);
            foreach (char c in afterColon)
            {
                if (c is < '0' or > '9')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Detects whether a host string contains a URI scheme prefix (e.g., <c>"amqps://"</c>, <c>"https://"</c>).
        /// </summary>
        /// <remarks>
        /// <para><b>Detection:</b> Uses a simple ordinal <c>"://"</c> substring search.  This is sufficient for
        /// configuration validation because no valid hostname or IP address contains <c>"://"</c>.  A full RFC 3986
        /// scheme parser is unnecessary for this purpose.</para>
        ///
        /// <para><b>Consumers:</b> <see cref="Configuration.RabbitMQOptions"/> reject host entries that contain URI
        /// schemes -- the scheme is implied by the transport configuration (e.g., <c>EnableSsl</c> for RabbitMQ, UDP
        /// for RADIUS).  Centralising the check here eliminates the duplicated <c>host.Contains("://")</c> pattern.</para>
        /// </remarks>
        /// <param name="host">The host string to inspect.  <see langword="null"/> and empty strings return
        /// <see langword="false"/>.</param>
        /// <returns><see langword="true"/> if the host contains a <c>"://"</c> scheme separator.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasUriScheme(string? host)
        {
            return !string.IsNullOrEmpty(host) && host.Contains("://", StringComparison.Ordinal);
        }

        /// <summary>
        /// Strips RFC 5952 section 6 bracket notation from an IPv6 literal if present.
        /// </summary>
        /// <remarks>
        /// <para>Configuration files and environment variables sometimes contain bracket-wrapped IPv6 addresses copied
        /// from URLs (e.g., <c>"[2001:db8::1]"</c>).  The brackets are a URI presentation format, not part of the
        /// address itself.  Leaving them in place causes double-bracketing in <c>host:port</c> formatting and may
        /// fail DNS resolution depending on the client library.</para>
        ///
        /// <para><b>Null/empty handling:</b> Returns the input unchanged when <see langword="null"/> or empty.  Callers
        /// are expected to validate emptiness separately -- this method is a normalisation pass, not a validation
        /// gate.</para>
        /// </remarks>
        /// <param name="host">The host string to normalise.</param>
        /// <returns>The host string with surrounding brackets removed if both <c>[</c> and <c>]</c> are present;
        /// otherwise the original string.  Returns <see langword="null"/> if <paramref name="host"/> is
        /// <see langword="null"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string? StripIPv6Brackets(string? host)
        {
            return string.IsNullOrEmpty(host) ? host : host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        }

        #endregion

    }
}
