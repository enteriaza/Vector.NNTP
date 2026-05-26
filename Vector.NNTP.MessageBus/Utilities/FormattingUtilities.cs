// FormattingUtilities.cs -- General-purpose, allocation-conscious formatting helpers for human-readable log output.
//
// Centralizes byte-count, bit-rate, duration, endpoint, and fixed-digit formatting so log output across the
// project uses consistent representations. All methods are static and thread-safe with no shared mutable state.
//
// Thread safety: All methods are static and stateless; inherently thread-safe.
// Cross-platform: Fully portable; uses only BCL APIs available on all .NET 8 runtimes (Windows x64, Linux x64).
// SIMD applicability: FormatTruncatedAsciiLine delegates to System.Text.Ascii.ToUtf16 (SIMD-accelerated on x64);
// all other methods perform scalar string formatting. No manual vectorization opportunities.
//

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Vector.NNTP.MessageBus.Utilities
{
    /// <summary>
    /// General-purpose, allocation-conscious formatting helpers for human-readable log output.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Centralizes human-readable formatting for byte counts, bit rates, durations, and network
    /// endpoints so all log output across the project uses consistent representations.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are static with no shared mutable state. They produce short,
    /// human-readable strings suitable for structured log parameters and diagnostic output. Safe for concurrent use
    /// from background services and thread-pool work items.</para>
    ///
    /// <para><b>Allocation:</b> Each formatting method allocates a single string for the return value. No intermediate
    /// collections or temporary buffers are created. The <see cref="StringBuilder"/>-accepting overloads enable callers
    /// building composite strings to avoid intermediate allocations. <see cref="FormatFixedDigits"/> is fully
    /// zero-allocation -- it writes directly into caller-supplied.</para>
    ///
    /// <para><b>Input validation:</b> All public entry points validate inputs eagerly and throw descriptive exceptions
    /// on failure. Throw paths are isolated into <c>ThrowHelper</c>-style methods to keep the fast path below the JIT
    /// inlining threshold.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable. All methods use BCL APIs available on all .NET 8 runtimes
    /// (Windows x64, Linux x64). No P/Invoke or OS-specific APIs.</para>
    /// </remarks>
    internal static class FormattingUtilities
    {

        #region Constants

        /// <summary>
        /// Number of bytes per kibibyte (1 KiB = 1 024 bytes).  Used as the boundary between the B and KB tiers
        /// in <see cref="FormatByteCount"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> 2^10 = 1 024.</para>
        /// </remarks>
        private const long BytesPerKB = 1_024L;

        /// <summary>
        /// Number of bytes per mebibyte (1 MiB = 1 048 576 bytes).  Used as the boundary between the KB and MB tiers
        /// in <see cref="FormatByteCount"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> <see cref="BytesPerKB"/> x 1 024 = 1 048 576.</para>
        /// </remarks>
        private const long BytesPerMB = BytesPerKB * 1_024;

        /// <summary>
        /// Number of bytes per gibibyte (1 GiB = 1 073 741 824 bytes).  Used as the boundary between the MB and GB tiers
        /// in <see cref="FormatByteCount"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> <see cref="BytesPerMB"/> x 1 024 = 1 073 741 824.</para>
        /// </remarks>
        private const long BytesPerGB = BytesPerMB * 1_024;

        /// <summary>
        /// Number of bytes per tebibyte (1 TiB = 1 099 511 627 776 bytes).  Used as the boundary between the GB and TB
        /// tiers in <see cref="FormatByteCount"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> <see cref="BytesPerGB"/> x 1 024 = 1 099 511 627 776.</para>
        /// </remarks>
        private const long BytesPerTB = BytesPerGB * 1_024;

        /// <summary>
        /// Bytes per gibibyte as a <see cref="double"/>, used for byte-to-GiB conversions in log output.  Extracted from
        /// the cache subsystem's <c>CacheFormatHelpers</c> to provide a single source of truth for GiB conversion.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> 1 024.0 x 1 024 x 1 024 = 1 073 741 824.0.  Expressed as <see cref="double"/>
        /// to avoid integer-to-float conversion on the division hot path in <see cref="ToGiB"/>.</para>
        /// </remarks>
        internal const double BytesPerGiB = 1_024.0 * 1_024 * 1_024;

        /// <summary>
        /// Minimum <see cref="TimeSpan.TotalSeconds"/> threshold below which <see cref="FormatBitRate"/> returns
        /// <c>"0 bps"</c> to avoid division by near-zero durations.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> 0.001 seconds (1 millisecond).  Durations shorter than this are considered
        /// instantaneous -- computing a bit rate over sub-millisecond intervals produces astronomically large and
        /// meaningless values that would clutter log output.</para>
        /// </remarks>
        private const double MinBitRateDurationSeconds = 0.001;

        /// <summary>
        /// Sentinel string returned by <see cref="FormatRemoteEndPoint"/> when the endpoint is <see langword="null"/>
        /// or not an <see cref="IPEndPoint"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> <c>"[unknown]"</c>.  Chosen to be visually distinct in log output and unambiguous as a
        /// non-address value.</para>
        /// </remarks>
        private const string UnknownEndPoint = "[unknown]";

        /// <summary>
        /// Bit-rate tier boundary: 1 000 bits per second.  Transitions from <c>bps</c> to <c>Kbps</c> in
        /// <see cref="FormatBitRate"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Unit system:</b> SI (decimal) prefixes -- powers of 1 000, not 1 024.  This is the standard
        /// convention in networking (e.g., 100 Mbps Ethernet = 100 x 10^6 bits/second).</para>
        /// </remarks>
        private const double BitsPerKilo = 1_000;

        /// <summary>
        /// Bit-rate tier boundary: 1 000 000 bits per second.  Transitions from <c>Kbps</c> to <c>Mbps</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> <see cref="BitsPerKilo"/> x 1 000 = 1 000 000.</para>
        /// </remarks>
        private const double BitsPerMega = 1_000_000;

        /// <summary>
        /// Bit-rate tier boundary: 1 000 000 000 bits per second.  Transitions from <c>Mbps</c> to <c>Gbps</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> <see cref="BitsPerMega"/> x 1 000 = 1 000 000 000.</para>
        /// </remarks>
        private const double BitsPerGiga = 1_000_000_000;

        /// <summary>
        /// Bit-rate tier boundary: 1 000 000 000 000 bits per second.  Transitions from <c>Gbps</c> to <c>Tbps</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>Derivation:</b> <see cref="BitsPerGiga"/> x 1 000 = 1 000 000 000 000.</para>
        /// </remarks>
        private const double BitsPerTera = 1_000_000_000_000;

        /// <summary>
        /// Estimated average character length of a single <c>host:port</c> entry in
        /// <see cref="FormatEndpointSummary"/>.  Used for <see cref="StringBuilder"/> capacity pre-computation to
        /// minimise buffer resizing.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> 22 characters.  A typical IPv4 entry (<c>198.18.0.70:5672</c>) is 16 characters; a
        /// typical hostname entry (<c>rabbit01a.usenet.dev:5672</c>) is 24 characters.  22 is a reasonable average
        /// that avoids both over-allocation for IPv4-only clusters and under-allocation for hostname-based
        /// configurations.</para>
        /// </remarks>
        private const int EstimatedHostPortLength = 22;

        /// <summary>
        /// Estimated average character length of a single <c>key=value</c> entry in <see cref="FormatKeyValuePairs"/>.
        /// Used for <see cref="StringBuilder"/> capacity pre-computation to minimise buffer resizing.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> 20 characters.  Typical entries (<c>context=BasicDeliver</c>) are 20-25 characters.
        /// Pre-sizing avoids the <see cref="StringBuilder"/>'s default 16-character capacity and the resulting
        /// reallocation on the first <c>Append</c> call.</para>
        /// </remarks>
        private const int EstimatedKeyValuePairLength = 20;

        /// <summary>
        /// Default maximum byte length for <see cref="FormatObjectValue"/> when the caller does not specify a cap.
        /// </summary>
        /// <remarks>
        /// <para><b>Value:</b> 256 bytes.  Large enough to capture typical AMQP client property values (product name,
        /// version string, platform identifier) without truncation, while capping the allocation for malicious or
        /// misconfigured oversized values.</para>
        /// </remarks>
        internal const int DefaultMaxByteLength = 256;

        #endregion

        #region Public Methods -- Byte / Bit-Rate Formatting

        /// <summary>
        /// Formats a byte count with the most appropriate binary-prefix unit (B, KB, MB, GB, TB).
        /// </summary>
        /// <param name="bytes">The byte count to format.  Must be non-negative.</param>
        /// <returns>A human-readable string such as <c>"967 B"</c>, <c>"4.210 KB"</c>, <c>"510.521 MB"</c>,
        /// <c>"1.234 GB"</c>, or <c>"2.048 TB"</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bytes"/> is negative.</exception>
        /// <remarks>
        /// <para><b>Unit system:</b> Uses binary prefixes (powers of 1 024) rather than SI prefixes (powers of 1 000).
        /// The labels <c>KB</c>, <c>MB</c>, <c>GB</c>, <c>TB</c> follow the convention commonly used in operating systems
        /// and network diagnostics, not the formal IEC 80000-13 labels (KiB, MiB, etc.).</para>
        ///
        /// <para><b>Precision:</b> Sub-byte tiers use three decimal places (<c>F3</c>) to match the precision expected in
        /// session summary logs where small differences (e.g., 4.210 KB vs. 4.211 KB) aid debugging.</para>
        ///
        /// <para><b>Tier selection:</b> A <c>switch</c> expression with ascending range patterns selects the appropriate
        /// unit.  The compiler generates an efficient jump table -- no sequential if/else chain.</para>
        /// </remarks>
        public static string FormatByteCount(long bytes)
        {
            if (bytes < 0)
                ThrowBytesNegative(bytes);
            return bytes switch
            {
                < BytesPerKB => $"{bytes} B",
                < BytesPerMB => $"{bytes / (double)BytesPerKB:F3} KB",
                < BytesPerGB => $"{bytes / (double)BytesPerMB:F3} MB",
                < BytesPerTB => $"{bytes / (double)BytesPerGB:F3} GB",
                _ => $"{bytes / (double)BytesPerTB:F3} TB",
            };
        }

        /// <summary>
        /// Converts a byte count to gibibytes for human-readable log output.
        /// </summary>
        /// <param name="bytes">The byte count to convert.</param>
        /// <returns>The equivalent value in GiB.</returns>
        /// <remarks>
        /// <para><b>Inlining:</b> Annotated with <see cref="MethodImplOptions.AggressiveInlining"/> -- the method body
        /// is a single floating-point division, well within the JIT inlining budget.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToGiB(long bytes)
        {
            return bytes / BytesPerGiB;
        }

        /// <summary>
        /// Computes the average throughput in bits per second and formats it with the most appropriate SI prefix
        /// (bps, Kbps, Mbps, Gbps, Tbps).
        /// </summary>
        /// <param name="bytes">Total bytes transferred.  Must be non-negative.</param>
        /// <param name="duration">The time period over which the bytes were transferred.  Must be non-negative.</param>
        /// <returns>A human-readable throughput string.  Returns <c>"0 bps"</c> when <paramref name="bytes"/> is zero
        /// or <paramref name="duration"/> is shorter than 1 millisecond.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bytes"/> is negative or
        /// <paramref name="duration"/> is negative.</exception>
        /// <remarks>
        /// <para><b>Unit system:</b> Uses SI (decimal) prefixes (powers of 1 000) for bit rates, which is the standard
        /// convention in networking (e.g., 100 Mbps Ethernet = 100 x 10^6 bits/second).</para>
        ///
        /// <para><b>Zero-duration guard:</b> Durations below <see cref="MinBitRateDurationSeconds"/> (1 ms) return
        /// <c>"0 bps"</c> to avoid division by near-zero producing meaninglessly large values in logs.</para>
        ///
        /// <para><b>Overflow safety:</b> <c>bytes * 8.0</c> promotes to <see cref="double"/> before the multiplication,
        /// preventing <see cref="long"/> overflow for byte counts up to <see cref="long.MaxValue"/>.  The maximum
        /// representable value (~9.2 x 10^18 bytes x 8 = ~7.4 x 10^19 bits) is well within <see cref="double"/>
        /// precision (~15.9 significant digits).</para>
        /// </remarks>
        public static string FormatBitRate(long bytes, TimeSpan duration)
        {
            if (bytes < 0)
                ThrowBytesNegative(bytes);
            if (duration < TimeSpan.Zero)
                ThrowDurationNegative(duration);
            if (duration.TotalSeconds < MinBitRateDurationSeconds || bytes == 0)
                return "0 bps";
            double bitsPerSecond = bytes * 8.0 / duration.TotalSeconds;
            return bitsPerSecond switch
            {
                < BitsPerKilo => $"{bitsPerSecond:F0} bps",
                < BitsPerMega => $"{bitsPerSecond / BitsPerKilo:F3} Kbps",
                < BitsPerGiga => $"{bitsPerSecond / BitsPerMega:F3} Mbps",
                < BitsPerTera => $"{bitsPerSecond / BitsPerGiga:F3} Gbps",
                _ => $"{bitsPerSecond / BitsPerTera:F3} Tbps",
            };
        }

        #endregion

        #region Public Methods -- Duration Formatting

        /// <summary>
        /// Formats a <see cref="TimeSpan"/> as <c>H:mm:ss</c> using total hours so durations exceeding 24 hours are
        /// represented correctly (e.g., <c>126:34:23</c> instead of <c>5.06:34:23</c>).
        /// </summary>
        /// <param name="duration">The duration to format.  Must be non-negative.</param>
        /// <returns>A string in <c>H:mm:ss</c> format where H is unbounded total hours.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="duration"/> is negative.</exception>
        /// <remarks>
        /// <para><b>Negative durations:</b> Negative values are rejected because casting a negative
        /// <see cref="TimeSpan.TotalHours"/> to <see cref="int"/> truncates toward zero while
        /// <see cref="TimeSpan.Minutes"/> and <see cref="TimeSpan.Seconds"/> return negative components, producing
        /// a misleading string such as <c>"0:-59:00"</c>.</para>
        ///
        /// <para><b>Inlining:</b> Annotated with <see cref="MethodImplOptions.AggressiveInlining"/>.  The validation
        /// guard delegates to <see cref="ThrowDurationNegative"/> (a <see cref="DoesNotReturnAttribute"/> method), so
        /// the fast path contains no <c>throw</c> instruction and is inlineable by the JIT.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
                ThrowDurationNegative(duration);
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        #endregion

        #region Public Methods -- Numeric Span Formatting

        /// <summary>
        /// Formats a fixed-width decimal integer directly into a byte span using right-to-left digit extraction.
        /// </summary>
        /// <param name="span">Destination buffer.  Must have at least <paramref name="digits"/> bytes available starting
        /// at <paramref name="offset"/>.</param>
        /// <param name="offset">Write position -- advanced by <paramref name="digits"/> on return.</param>
        /// <param name="value">The non-negative integer value to format.  Behaviour is undefined for negative
        /// values.</param>
        /// <param name="digits">The exact number of digits to emit.  If <paramref name="value"/> has fewer digits than
        /// <paramref name="digits"/>, the result is zero-padded on the left (e.g., month <c>3</c> with
        /// <paramref name="digits"/>=2 produces <c>03</c>).</param>
        /// <remarks>
        /// <para><b>Algorithm:</b> Extracts digits right-to-left using <c>value % 10</c> / <c>value /= 10</c> and writes
        /// each as <c>(byte)('0' + digit)</c>.  This is branchless within the loop body and compiles to a tight sequence of
        /// <c>div</c> + <c>mov</c> instructions on x64.</para>
        ///
        /// <para><b>Zero allocation:</b> Writes directly into the caller-supplied <see cref="Span{T}"/>.  No heap
        /// allocation occurs.</para>
        ///
        /// <para><b>Primary consumer:</b> Session-level formatting for fixed-width timestamps where each component has
        /// a known fixed width.</para>
        ///
        /// <para><b>Cross-platform:</b> Fully portable.  Uses only integer arithmetic and <see cref="Span{T}"/>
        /// indexing -- no P/Invoke, no OS-specific APIs, no endianness dependence.</para>
        ///
        /// <para><b>SIMD applicability:</b> Not applicable.  Fixed-digit formatting operates on 2-4 digit values
        /// where scalar division is optimal and vectorisation overhead would exceed the computation cost.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FormatFixedDigits(Span<byte> span, ref int offset, int value, int digits)
        {
            for (int i = digits - 1; i >= 0; i--)
            {
                span[offset + i] = (byte)('0' + (value % 10));
                value /= 10;
            }
            offset += digits;
        }

        /// <summary>
        /// Formats a non-negative <see cref="long"/> value as a variable-width decimal string directly into a byte span
        /// using right-to-left digit extraction.
        /// </summary>
        /// <param name="span">Destination buffer.  Must have at least <paramref name="digitCount"/> bytes available
        /// starting at <paramref name="offset"/>.</param>
        /// <param name="offset">Write position -- advanced by <paramref name="digitCount"/> on return.</param>
        /// <param name="value">The non-negative integer value to format.  Negative values are clamped to zero to prevent
        /// silent data corruption (writing bytes below <c>'0'</c>) in the output buffer.</param>
        /// <param name="digitCount">The number of digits to emit.  If <paramref name="value"/> has fewer digits than
        /// <paramref name="digitCount"/>, the result is zero-padded on the left.</param>
        /// <remarks>
        /// <para><b>Algorithm:</b> Identical to <see cref="FormatFixedDigits"/> -- extracts digits right-to-left using
        /// <c>value % 10</c> / <c>value /= 10</c> and writes each as <c>(byte)('0' + digit)</c>.  This is branchless
        /// within the loop body and compiles to a tight sequence of <c>div</c> + <c>mov</c> instructions on x64.</para>
        ///
        /// <para><b>Zero allocation:</b> Writes directly into the caller-supplied <see cref="Span{T}"/>.  No heap
        /// allocation occurs.</para>
        ///
        /// <para><b>Negative value guard:</b> Negative values are clamped to zero rather than allowing
        /// <c>value % 10</c> to produce negative digits.  Without this guard, a negative <see cref="long"/> would write
        /// bytes below <c>'0'</c> (e.g., <c>(byte)('0' + (-3)) = 0x2D</c>), silently corrupting the NNTP response
        /// line.  The guard writes <c>"0"</c> (zero-padded to <paramref name="digitCount"/>) which produces an obviously
        /// incorrect but safe response rather than a protocol-violating byte sequence.</para>
        ///
        /// <para><b>Provenance:</b> Extracted to centralize variable-width long formatting alongside fixed-width int
        /// formatting.  Used for formatting numeric values directly into byte spans.</para>
        ///
        /// <para><b>Consumers:</b> Formatting helpers throughout the application that need to emit numeric values
        /// directly into spans.</para>
        ///
        /// <para><b>Cross-platform:</b> Fully portable.  Uses only integer arithmetic and <see cref="Span{T}"/>
        /// indexing -- no P/Invoke, no OS-specific APIs, no endianness dependence.</para>
        ///
        /// <para><b>SIMD applicability:</b> Not applicable.  Variable-width formatting operates on 1-19 digit values
        /// where scalar division is optimal and vectorization overhead would exceed the computation cost.</para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FormatVariableDigits(Span<byte> span, ref int offset, long value, int digitCount)
        {
            // Guard: clamp negative values to zero to prevent writing bytes below '0' into the output buffer.
            if (value < 0)
                value = 0;
            for (int i = digitCount - 1; i >= 0; i--)
            {
                span[offset + i] = (byte)('0' + (value % 10));
                value /= 10;
            }
            offset += digitCount;
        }

        #endregion

        #region Public Methods -- Endpoint Formatting

        /// <summary>
        /// Formats a remote endpoint for structured logging.
        /// </summary>
        /// <param name="endPoint">The remote endpoint to format.  May be <see langword="null"/>.</param>
        /// <returns><c>[addr]:port</c> for IPv6 (brackets per RFC 5952 S6), <c>addr:port</c> for IPv4, or
        /// <c>"[unknown]"</c> if the endpoint is <see langword="null"/> or not an <see cref="IPEndPoint"/>.</returns>
        /// <remarks>
        /// <para><b>IPv4-mapped IPv6:</b> Addresses like <c>::ffff:127.0.0.1</c> are mapped to their IPv4 representation
        /// via <see cref="NormaliseAddress"/> for readability in logs.  This ensures log output consistently shows
        /// <c>127.0.0.1:119</c> rather than <c>[::ffff:127.0.0.1]:119</c> regardless of the socket's address
        /// family.</para>
        ///
        /// <para><b>Null safety:</b> Returns <see cref="UnknownEndPoint"/> (<c>"[unknown]"</c>) for <see langword="null"/>
        /// or non-<see cref="IPEndPoint"/> values, allowing callers to pass <see cref="SafeGetRemoteEndPoint"/> results
        /// directly without null-checking.</para>
        /// </remarks>
        public static string FormatRemoteEndPoint(EndPoint? endPoint)
        {
            if (endPoint is not IPEndPoint ipep)
                return UnknownEndPoint;
            IPAddress address = NormaliseAddress(ipep.Address);
            return (address.AddressFamily == AddressFamily.InterNetworkV6)
                ? $"[{address}]:{ipep.Port}"
                : $"{address}:{ipep.Port}";
        }

        /// <summary>
        /// Formats a <c>host:port</c> string with correct IPv6 bracketing per RFC 2732.  IPv6 addresses are enclosed in
        /// brackets (<c>[fd12::1]:5012</c>); IPv4 addresses and hostnames use the standard <c>host:port</c> format
        /// (<c>10.0.0.4:5012</c>).
        /// </summary>
        /// <param name="host">The IP address or hostname.  May be an IPv4 address (<c>10.0.0.4</c>), an IPv6 address
        /// (<c>fd12::1</c>), or a DNS hostname (<c>node-a.cluster.local</c>).</param>
        /// <param name="port">The TCP port number.</param>
        /// <returns>A correctly bracketed <c>host:port</c> string.</returns>
        /// <remarks>
        /// <para><b>IPv6 detection:</b> Attempts to parse the host as an IP address to determine if it is an IPv6 literal.
        /// If the parse fails (DNS hostname), the address is treated as non-IPv6 -- hostnames never require bracketing.
        /// This method is only used for diagnostic logging, not on hot paths.</para>
        /// </remarks>
        public static string FormatHostPort(string host, int port)
        {
            return IPAddress.TryParse(host, out IPAddress? ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{host}]:{port}"
                : $"{host}:{port}";
        }

        /// <summary>
        /// Appends a <c>host:port</c> string with correct IPv6 bracketing to the specified <see cref="StringBuilder"/>.
        /// This is the allocation-free overload of <see cref="FormatHostPort"/> for callers that are building composite
        /// strings (e.g., comma-separated endpoint lists).
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> to append to.  Must not be <see langword="null"/>.</param>
        /// <param name="host">The IP address or hostname.</param>
        /// <param name="port">The TCP port number.</param>
        /// <remarks>
        /// <para><b>Culture invariance:</b> Integer formatting of the port number is inherently culture-invariant -- no
        /// locale alters the digit representation of an <see cref="int"/>.  <see cref="StringBuilder.Append(int)"/> is
        /// used rather than interpolation to avoid an intermediate <see cref="string"/> allocation for the port
        /// number.</para>
        /// </remarks>
        public static void AppendHostPort(StringBuilder sb, string host, int port)
        {
            _ = IPAddress.TryParse(host, out IPAddress? ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
                ? sb.Append('[').Append(host).Append("]:")
                : sb.Append(host).Append(':');
            _ = sb.Append(port);
        }

        /// <summary>
        /// Builds a comma-separated endpoint summary string from the given hosts and port for use in structured log
        /// messages.
        /// </summary>
        /// <param name="hosts">The host array (IP addresses or hostnames).  Must not be <see langword="null"/>.</param>
        /// <param name="port">The port number to append to each host.</param>
        /// <returns>A formatted string such as <c>"198.18.0.70:5672, 198.18.0.71:5672"</c>.  Returns an empty string
        /// when <paramref name="hosts"/> is empty.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="hosts"/> is
        /// <see langword="null"/>.</exception>
        /// <remarks>
        /// <para><b>Output format:</b> Produces output such as <c>"198.18.0.70:5672, 198.18.0.71:5672, 198.18.0.72:5672"</c>
        /// for a 3-node cluster.  IPv6 addresses are wrapped in RFC 5952 S6 bracket notation to avoid ambiguity with the
        /// port separator (e.g., <c>"[2001:db8::1]:5672"</c>).</para>
        ///
        /// <para><b>Input contract:</b> Host entries are expected to be normalized (trimmed, bracket-stripped, validated
        /// for format and DNS resolution).  No defensive normalization or null-checking of individual entries is performed
        /// here.</para>
        ///
        /// <para><b>Capacity pre-sizing:</b> Uses a <see cref="StringBuilder"/> with pre-computed capacity
        /// (<see cref="EstimatedHostPortLength"/> x host count) for allocation-efficient concatenation when the host list
        /// contains more than one entry.  For a single-host configuration, the <see cref="StringBuilder"/> overhead is
        /// negligible and the code path is simpler than a conditional fast-path.</para>
        /// </remarks>
        public static string FormatEndpointSummary(string[] hosts, int port)
        {
            ArgumentNullException.ThrowIfNull(hosts);
            // Pre-compute capacity: "host:port" ~= average 22 chars + ", " separator.
            StringBuilder sb = new(hosts.Length * EstimatedHostPortLength);
            for (int i = 0; i < hosts.Length; i++)
            {
                if (i > 0)
                    _ = sb.Append(", ");
                AppendHostPort(sb, hosts[i], port);
            }
            return sb.ToString();
        }

        #endregion

        #region Public Methods -- Key-Value Pair Formatting

        /// <summary>
        /// Formats a dictionary of key-value pairs as a comma-separated <c>key=value</c> string for structured log
        /// output.
        /// </summary>
        /// <param name="pairs">The dictionary to format.  May be <see langword="null"/> or empty.</param>
        /// <returns>A comma-separated string such as <c>"context=BasicDeliver, channel=1"</c>.  Returns
        /// <c>"(none)"</c> when <paramref name="pairs"/> is <see langword="null"/> or empty.</returns>
        /// <remarks>
        /// <para><b>Purpose:</b> Centralizes the <c>key=value, key=value</c> formatting pattern used in event handler
        /// diagnostic logging.  Without this method, each handler that needs to format a dictionary (e.g., the RabbitMQ
        /// <c>CallbackExceptionAsync</c> handler's <c>Detail</c> dictionary) would inline its own
        /// <see cref="StringBuilder"/> loop -- duplicating the separator logic and capacity estimation.</para>
        ///
        /// <para><b>Capacity pre-sizing:</b> Uses <see cref="EstimatedKeyValuePairLength"/> (20 characters) per entry to
        /// pre-size the <see cref="StringBuilder"/>, minimizing buffer resizing for typical dictionaries with 1-5
        /// entries.</para>
        ///
        /// <para><b>Null/empty guard:</b> Returns the sentinel <c>"(none)"</c> for <see langword="null"/> or empty
        /// dictionaries, providing a meaningful default for structured log parameters without requiring callers to
        /// implement their own null-check branches.</para>
        ///
        /// <para><b>Thread safety:</b> Stateless.  Allocates only a local <see cref="StringBuilder"/> and the return
        /// <see cref="string"/>.  Safe for concurrent use.</para>
        ///
        /// <para><b>Consumers:</b></para>
        /// <list type="bullet">
        ///   <item><description>Endpoint formatting helpers throughout the application.</description></item>
        /// </list>
        /// </remarks>
        public static string FormatKeyValuePairs(IDictionary<string, object>? pairs)
        {
            if (pairs is null or { Count: 0 })
                return "(none)";
            StringBuilder sb = new(pairs.Count * EstimatedKeyValuePairLength);
            foreach (KeyValuePair<string, object> kvp in pairs)
            {
                if (sb.Length > 0)
                    _ = sb.Append(", ");
                _ = sb.Append(kvp.Key);
                _ = sb.Append('=');
                _ = sb.Append(kvp.Value);
            }
            return sb.ToString();
        }

        #endregion

        #region Public Methods -- Address Normalisation

        /// <summary>
        /// Normalizes an <see cref="IPAddress"/>, mapping IPv4-mapped IPv6 addresses to their IPv4 representation.
        /// </summary>
        /// <param name="address">The address to normalize.  Must not be <see langword="null"/>.</param>
        /// <returns>The IPv4 form if the address is IPv4-mapped IPv6; otherwise the original address.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="address"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para><b>Rationale:</b> Dual-stack sockets on Linux and Windows accept IPv4 connections as IPv4-mapped IPv6
        /// addresses (<c>::ffff:a.b.c.d</c>).  Normalising to IPv4 produces cleaner log output, simpler ACL matching,
        /// and consistent behaviour regardless of socket configuration.</para>
        ///
        /// <para><b>Cross-platform:</b> <see cref="IPAddress.IsIPv4MappedToIPv6"/> and <see cref="IPAddress.MapToIPv4"/>
        /// are BCL APIs that behave identically on Windows and Linux.  On Linux, dual-stack sockets are the default
        /// (unless <c>net.ipv6.bindv6only=1</c> is set); on Windows, they are enabled via
        /// <see cref="Socket.DualMode"/>.</para>
        ///
        /// <para><b>Consumers:</b></para>
        /// <list type="bullet">
        ///   <item><description><see cref="FormatRemoteEndPoint"/> -- IPv4-mapped IPv6 normalisation for log
        ///     output.</description></item>
        ///   <item><description>IP comparison and validation helpers throughout the application.</description></item>
        /// </list>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IPAddress NormaliseAddress(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }

        #endregion

        #region Public Methods -- Socket Helpers

        /// <summary>
        /// Safely reads <see cref="Socket.RemoteEndPoint"/>, returning <see langword="null"/> if the socket has
        /// already been disposed or reset by the remote peer.
        /// </summary>
        /// <param name="socket">The socket to query.  Must not be <see langword="null"/>.</param>
        /// <returns>The remote endpoint, or <see langword="null"/> if an <see cref="ObjectDisposedException"/> or
        /// <see cref="SocketException"/> is thrown.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="socket"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para><b>Failure modes:</b> <see cref="Socket.RemoteEndPoint"/> can throw <see cref="ObjectDisposedException"/>
        /// if the socket has been disposed, or <see cref="SocketException"/> if the peer reset the connection between
        /// accept completion and the log call.  This helper catches only those two expected exceptions, preventing an
        /// unhandled exception from propagating into IOCP callback threads.</para>
        ///
        /// <para><b>Design choice -- narrow catch:</b> A bare <c>catch</c> would also swallow fatal exceptions such as
        /// <see cref="OutOfMemoryException"/>.  By catching only <see cref="SocketException"/> and
        /// <see cref="ObjectDisposedException"/>, programming errors and resource-exhaustion failures propagate
        /// correctly.</para>
        ///
        /// <para><b>Placement note:</b> This method is a socket operation for safely reading remote endpoints from sockets.
        /// Callers typically chain this with <see cref="FormatRemoteEndPoint"/> to format the result.</para>
        /// </remarks>
        public static EndPoint? SafeGetRemoteEndPoint(Socket socket)
        {
            ArgumentNullException.ThrowIfNull(socket);
            try
            {
                return socket.RemoteEndPoint;
            }
            catch (ObjectDisposedException) { return null; }
            catch (SocketException) { return null; }
        }

        #endregion

        #region Public Methods -- Object Value Formatting

        /// <summary>
        /// Converts an <see cref="object"/> value to a human-readable string, decoding <c>byte[]</c> values as UTF-8 with
        /// a configurable length cap to prevent excessive memory allocation from oversized values.
        /// </summary>
        /// <param name="value">The value to format -- either <c>byte[]</c>, <c>string</c>, or any other CLR type.  May be
        /// <see langword="null"/>.</param>
        /// <param name="maxByteLength">Maximum byte length to decode from a <c>byte[]</c> value.  Values exceeding this
        /// length are truncated and appended with <c>"...(truncated)"</c>.  Must be positive.  Defaults to
        /// <see cref="DefaultMaxByteLength"/> (256).</param>
        /// <returns>A human-readable string representation of the value.  Never <see langword="null"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxByteLength"/> is less than or
        /// equal to zero.</exception>
        /// <remarks>
        /// <para><b>Purpose:</b> Centralises the "format an unknown object with special byte[] UTF-8 handling" pattern
        /// used in AMQP client property diagnostics and any other context where protocol-level table values may be
        /// <c>byte[]</c> (AMQP long-strings), CLR <c>string</c>s, or <see langword="null"/>.  Without this method, each
        /// call site that encounters mixed-type protocol values must duplicate the type-switch, truncation, and null
        /// handling logic.</para>
        ///
        /// <para><b>byte[] decoding:</b> The AMQP 0-9-1 wire format encodes long-strings as <c>byte[]</c> on the .NET
        /// side.  Without decoding, these render as <c>System.Byte[]</c> (via <see cref="object.ToString"/>) or hex dumps
        /// (via log destructuring).  This method detects <c>byte[]</c> values and decodes them as UTF-8.</para>
        ///
        /// <para><b>Security -- length cap:</b> The <c>byte[]</c> value is capped at <paramref name="maxByteLength"/>
        /// before UTF-8 decoding.  A malicious or misconfigured source could provide oversized table values; without the
        /// cap, decoding such a value would allocate an arbitrarily large string during diagnostic logging.</para>
        ///
        /// <para><b>Exception safety:</b> The <see langword="null"/> arm is handled explicitly in the <c>switch</c>
        /// expression, so <see cref="Encoding.UTF8"/>.<see cref="Encoding.GetString(byte[])"/> is never called with a
        /// <see langword="null"/> argument.  The <c>_</c> default arm calls <see cref="object.ToString"/> which may return
        /// <see langword="null"/> for custom implementations -- handled by the <c>?? "(null)"</c> fallback.
        /// <see cref="DecoderFallbackException"/> from malformed UTF-8 byte sequences is caught and returns a safe
        /// <c>"(invalid UTF-8, N bytes)"</c> placeholder to prevent unhandled exceptions during diagnostic
        /// logging.</para>
        ///
        /// <para><b>Thread safety:</b> Stateless.  Allocates only the return <see cref="string"/>.  Safe for concurrent
        /// use.</para>
        ///
        /// <para><b>Cross-platform:</b> Fully portable.  <see cref="Encoding.UTF8"/> is a BCL API available on all .NET 8
        /// runtimes (Windows x64, Linux x64).</para>
        ///
        /// <para><b>SIMD applicability:</b> Not applicable.  UTF-8 decoding for typical values (&lt;100 bytes) does not
        /// benefit from manual vectorisation -- <see cref="Encoding.UTF8"/> already uses optimised internal
        /// paths.</para>
        ///
        /// <para><b>Consumers:</b></para>
        /// <list type="bullet">
        ///   <item><description>AMQP client property value formatting and other object value formatting throughout the
        ///     application.</description></item>
        /// </list>
        /// </remarks>
        public static string FormatObjectValue(object? value, int maxByteLength = DefaultMaxByteLength)
        {
            if (maxByteLength <= 0)
                ThrowMaxByteLengthNonPositive(maxByteLength);
            return value switch
            {
                byte[] bytes => FormatByteArrayValue(bytes, maxByteLength),
                null => "(null)",
                _ => value.ToString() ?? "(null)",
            };
        }

        #endregion

        #region Private Methods -- Object Value Formatting

        /// <summary>
        /// Decodes a <c>byte[]</c> as UTF-8 with truncation and exception safety for malformed sequences.
        /// </summary>
        /// <param name="bytes">The byte array to decode.  Must not be <see langword="null"/>.</param>
        /// <param name="maxByteLength">Maximum number of bytes to decode before truncating.</param>
        /// <returns>The decoded UTF-8 string, optionally truncated, or a safe placeholder if the bytes contain
        /// malformed UTF-8 sequences.</returns>
        /// <remarks>
        /// <para><b>Exception safety:</b> Catches <see cref="DecoderFallbackException"/> which can be thrown by
        /// <see cref="Encoding.UTF8"/> when the byte array contains invalid UTF-8 sequences (e.g., malformed AMQP
        /// long-string values from misbehaving clients).  Returns a descriptive placeholder string rather than
        /// allowing the exception to propagate into diagnostic logging paths.</para>
        /// </remarks>
        private static string FormatByteArrayValue(byte[] bytes, int maxByteLength)
        {
            try
            {
                return bytes.Length <= maxByteLength
                    ? Encoding.UTF8.GetString(bytes)
                    : $"{Encoding.UTF8.GetString(bytes, 0, maxByteLength)}...(truncated)";
            }
            catch (DecoderFallbackException)
            {
                return $"(invalid UTF-8, {bytes.Length} bytes)";
            }
        }

        #endregion

        #region Throw Helpers

        /// <summary>
        /// Throws <see cref="ArgumentOutOfRangeException"/> for a negative byte count.  Isolated into a separate method
        /// so the fast path in <see cref="FormatByteCount"/> and <see cref="FormatBitRate"/> remains below the JIT
        /// inlining threshold.
        /// </summary>
        /// <param name="bytes">The invalid byte count (for the exception message).</param>
        [DoesNotReturn]
        private static void ThrowBytesNegative(long bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Byte count must be non-negative.");
        }

        /// <summary>
        /// Throws <see cref="ArgumentOutOfRangeException"/> for a negative duration.  Isolated into a separate method
        /// so the fast path in <see cref="FormatBitRate"/> and <see cref="FormatDuration"/> remains below the JIT
        /// inlining threshold.
        /// </summary>
        /// <param name="duration">The invalid duration (for the exception message).</param>
        [DoesNotReturn]
        private static void ThrowDurationNegative(TimeSpan duration)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be non-negative.");
        }

        /// <summary>
        /// Throws <see cref="ArgumentOutOfRangeException"/> for a non-positive <c>maxByteLength</c> parameter.  Isolated
        /// into a separate method to keep the fast path in <see cref="FormatObjectValue"/> below the JIT inlining
        /// threshold.
        /// </summary>
        /// <param name="maxByteLength">The invalid maximum byte length (for the exception message).</param>
        [DoesNotReturn]
        private static void ThrowMaxByteLengthNonPositive(int maxByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maxByteLength), maxByteLength, "Maximum byte length must be positive.");
        }

        #endregion

    }
}
