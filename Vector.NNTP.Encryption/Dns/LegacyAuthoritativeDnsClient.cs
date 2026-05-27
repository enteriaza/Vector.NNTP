// <copyright file="LegacyAuthoritativeDnsClient.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AuthoritativeDnsClient.cs — Minimal DNS client that sends raw UDP TXT queries directly to authoritative
// nameserver IPs, bypassing recursive resolvers.  Used exclusively by AcmeCertificateProvider to poll
// _acme-challenge TXT records during ACME DNS-01 validation.
//
// Why not DnsClient NuGet?
//   The previous implementation used DnsClient (1.8.0) solely for TXT record lookups against
//   authoritative nameservers.  That package provides a full-featured recursive resolver client
//   with caching, TCP fallback, retries, EDNS0, and DNSSEC — none of which are needed here.
//   This class implements only the minimal DNS wire protocol subset required: a single-question
//   UDP query for TXT records with response parsing.  This eliminates a 450 KB dependency and
//   its transitive System.Buffers/System.Memory references.
//
// DNS wire protocol (RFC 1035):
//   Query:  12-byte header (random ID, QR=0, RD=0, QDCOUNT=1) + QNAME + QTYPE=TXT + QCLASS=IN
//   Response validation: ID match, QR=1, TC=0, RCODE=0
//   Answer parsing: skip question section, iterate answer RRs, extract TXT RDATA character-strings
//
// Methods:
//   QueryTxtAsync      -- Sends a UDP TXT query to a randomly-selected nameserver and parses the response.
//   BuildTxtQuery      -- Constructs a DNS query packet with validated, label-encoded QNAME.
//   ParseTxtResponse   -- Validates the response header and extracts TXT record values from answer RRs.
//   ParseTxtRdata      -- Parses a single TXT RR's RDATA into a concatenated character-string.
//   TrySkipName        -- Advances past a DNS name (inline labels or compression pointers) in wire format.
//
// Limitations (acceptable for this use case):
//   - UDP only — no TCP fallback for responses > 512 bytes.  ACME challenge TXT values are
//     43 bytes (base64url-encoded SHA-256), so responses are always well under 512 bytes.
//   - No EDNS0 — the query does not include an OPT record.  Not needed for small TXT responses.
//   - No recursion — the RD (Recursion Desired) bit is cleared because the target servers are
//     authoritative for the zone.
//   - No retry — the caller (WaitForTxtRecordAsync) already retries at DnsPollInterval.
//   - No DNSSEC validation — the response is used only to confirm record visibility before
//     triggering ACME validation; authenticity is verified by Let's Encrypt, not by us.
//
// Security:
//   - Query IDs are randomised per request to match responses and mitigate off-path spoofing.
//     Acceptable because the response is used only for propagation timing, not for security
//     decisions — Let's Encrypt independently validates the challenge.
//   - DNS label encoding validates label lengths (1-63 bytes per RFC 1035 §2.3.4) and total
//     QNAME length (<=255 bytes per RFC 1035 §3.1) to prevent buffer over-allocation from
//     malformed input.
//   - Label content is validated as pure ASCII via EncodingUtilities.IsAscii before encoding,
//     and encoded via EncodingUtilities.AsciiToSpan which throws on non-ASCII input — providing
//     a double-safety net against silent character substitution.
//   - Response parsing bounds-checks all offset advances against the buffer length.  Malformed
//     responses cause an early return rather than an out-of-bounds read.
//   - The compression pointer loop counter prevents infinite loops from circular pointers in
//     adversarial responses.
//   - UdpClient is created per-call and disposed deterministically via 'using', preventing
//     socket handle leaks on timeout, cancellation, or exception paths.
//   - The query packet is built using stackalloc when the name fits within a conservative
//     threshold (MaxStackAllocQuerySize), eliminating heap allocation for all realistic ACME
//     challenge domain names.
//
// Platform compatibility:
//   Linux and Windows (x64).  No platform-specific APIs are used.  UdpClient, Socket, and
//   CancellationTokenSource behave identically on both platforms under .NET 8.
//   AddressFamily is derived from the selected nameserver IP at runtime, supporting both
//   IPv4 (InterNetwork) and IPv6 (InterNetworkV6) transparently.
//
// SIMD applicability:
//   Not applicable.  Query construction involves small label-by-label copies (1-63 bytes each)
//   via EncodingUtilities.AsciiToSpan (which delegates to System.Text.Ascii internally).
//   Response parsing performs scalar offset arithmetic and 16-bit big-endian reads.  Neither
//   path processes contiguous buffers large enough to benefit from vector instructions —
//   typical DNS packets are 50-200 bytes.
//
// Allocation profile:
//   QueryTxtAsync:      One UdpClient (disposed per-call), one linked CancellationTokenSource
//                        (disposed per-call), one query byte[] (stackalloc fast-path avoids this
//                        for names <= MaxStackAllocQuerySize), one UdpReceiveResult (runtime-owned
//                        buffer), one List<string> result.
//   BuildTxtQuery:      One string[] from name.Split('.') — unavoidable without a custom label
//                        parser.  The query packet uses stackalloc when possible.
//   ParseTxtResponse:   One List<string> (capacity 0 — grows only if TXT answers are present).
//                        Single-segment TXT fast path: one string allocation.  Multi-segment
//                        fallback: one StringBuilder + one string.
//   ParseTxtRdata:      Fast path: one string.  Multi-segment: one StringBuilder + one string.
//   TrySkipName:        Zero allocations — pure offset arithmetic.
//
// Callers:
//   AcmeCertificateProvider.WaitForTxtRecordAsync — polls until the TXT record is visible.

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Vector.NNTP.Utilities.Encoding;

namespace Vector.NNTP.Encryption.Dns
{

    /// <summary>
    /// Legacy UDP-only DNS client retained for the optional Cloudflare NS fallback path in
    /// <see cref="Certificates.Acme.AcmeCertificateProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Wire protocol:</b> Constructs a standards-compliant DNS query packet (RFC 1035 §4) with a single TXT
    /// question, sends it via UDP to the specified nameservers, and parses TXT RDATA from the response.  The query ID is
    /// randomised per request to match responses and provide minimal protection against off-path spoofing (acceptable
    /// because the response is used only for propagation timing, not for security decisions).</para>
    ///
    /// <para><b>Nameserver rotation:</b> When multiple nameserver IPs are provided, each call to
    /// <see cref="QueryTxtAsync"/> selects one at random.  This spreads queries across all authoritative servers (important
    /// for Cloudflare anycast deployments where different PoPs may propagate at different speeds) without the complexity of
    /// round-robin state tracking.</para>
    ///
    /// <para><b>Timeout:</b> A 5-second timeout via <see cref="CancellationTokenSource.CancelAfter(int)"/> on a linked
    /// <see cref="CancellationTokenSource"/> prevents indefinite blocking if the nameserver doesn't respond.  The caller's
    /// poll loop retries on failure, so a single timeout is not critical.</para>
    ///
    /// <para><b>Thread safety:</b> Each call to <see cref="QueryTxtAsync"/> creates a fresh <see cref="UdpClient"/> with
    /// no shared mutable state.  Safe for concurrent calls (though the sole caller is single-threaded).</para>
    ///
    /// <para><b>Input validation:</b> <see cref="BuildTxtQuery"/> validates DNS label lengths (1-63 bytes) and total QNAME
    /// length (≤255 bytes) per RFC 1035 §2.3.4 and §3.1.  Empty labels (caused by consecutive dots in the input, e.g.
    /// <c>example..com</c>) are rejected to prevent malformed queries.  Label content is validated as pure ASCII via
    /// <see cref="EncodingUtilities.IsAscii(ReadOnlySpan{char})"/> before encoding, and encoded via
    /// <see cref="EncodingUtilities.AsciiToSpan"/> which throws on non-ASCII input -- providing a double-safety net
    /// against silent character substitution.</para>
    ///
    /// <para><b>Robustness:</b> <see cref="ParseTxtResponse"/> and <see cref="TrySkipName"/> bounds-check all buffer
    /// accesses and cap compression pointer traversals to prevent infinite loops from adversarial responses.  Malformed
    /// packets produce an empty result rather than exceptions.</para>
    ///
    /// <para><b>Resource lifecycle:</b> <see cref="UdpClient"/> and <see cref="CancellationTokenSource"/> are both
    /// disposed deterministically via <see langword="using"/> declarations in <see cref="QueryTxtAsync"/>, preventing
    /// socket handle leaks on timeout, cancellation, or exception paths.</para>
    ///
    /// <para><b>Platform compatibility:</b> Uses only platform-agnostic .NET APIs (<see cref="UdpClient"/>,
    /// <see cref="Socket"/>, <see cref="CancellationTokenSource"/>).  Compatible with both Linux and Windows on x64.
    /// <see cref="AddressFamily"/> is derived from the selected nameserver IP at runtime, supporting IPv4 and IPv6
    /// transparently.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  Query construction involves small label-by-label copies
    /// (1-63 bytes each).  Response parsing performs scalar offset arithmetic and 16-bit big-endian reads.  Typical DNS
    /// packets are 50-200 bytes -- far below the threshold where vector instructions would amortise their setup
    /// cost.</para>
    /// </remarks>
    internal sealed class LegacyAuthoritativeDnsClient
    {
        #region Constants

        /// <summary>
        /// Timeout applied to the <see cref="UdpClient.ReceiveAsync(CancellationToken)"/> call via a linked
        /// <see cref="CancellationTokenSource"/>.  5 seconds is generous for a single UDP round-trip to an authoritative
        /// nameserver (typical RTT: 5-50 ms).  If the server doesn't respond, the caller's poll loop will retry.
        /// </summary>
        private const int ReceiveTimeoutMs = 5_000;

        /// <summary>DNS TXT record type value (RFC 1035 §3.2.2).</summary>
        private const ushort DnsTypeTxt = 16;

        /// <summary>DNS IN (Internet) class value (RFC 1035 §3.2.4).</summary>
        private const ushort DnsClassIn = 1;

        /// <summary>
        /// Fixed DNS header size in bytes (RFC 1035 §4.1.1):
        /// ID (2) + Flags (2) + QDCOUNT (2) + ANCOUNT (2) + NSCOUNT (2) + ARCOUNT (2).
        /// </summary>
        private const int DnsHeaderSize = 12;

        /// <summary>
        /// Fixed size of the per-RR fields between the NAME and RDATA in an answer RR (RFC 1035 §4.1.3):
        /// TYPE (2) + CLASS (2) + TTL (4) + RDLENGTH (2).
        /// </summary>
        private const int RrFixedFieldsSize = 10;

        /// <summary>
        /// Maximum number of DNS name compression pointer hops allowed before aborting name traversal.  Prevents infinite
        /// loops from adversarial or corrupted responses containing circular compression pointers (e.g. pointer A → B → A).
        /// </summary>
        /// <remarks>
        /// A legitimate DNS name has at most 127 labels (255 bytes / 2 bytes per minimal label), so 128 hops is a generous
        /// upper bound that will never be reached by valid packets.
        /// </remarks>
        private const int MaxCompressionPointerHops = 128;

        /// <summary>
        /// Maximum DNS QNAME length in bytes (including the trailing root label), per RFC 1035 §3.1.  A domain name is
        /// limited to 255 octets in wire format.
        /// </summary>
        private const int MaxQnameLength = 255;

        /// <summary>
        /// Maximum length of a single DNS label (RFC 1035 §2.3.4).  Labels are limited to 63 octets.
        /// </summary>
        private const int MaxLabelLength = 63;

        /// <summary>
        /// Maximum total query packet size eligible for <c>stackalloc</c> allocation.  Computed as:
        /// <see cref="DnsHeaderSize"/> (12) + <see cref="MaxQnameLength"/> (255) + QTYPE (2) + QCLASS (2) = 271 bytes.
        /// </summary>
        /// <remarks>
        /// <para>All valid DNS query packets for a single-question TXT lookup fit within this size.  The <c>stackalloc</c>
        /// fast path in <see cref="BuildTxtQuery"/> avoids a heap <c>byte[]</c> allocation for every query -- significant
        /// when polling at <c>DnsPollInterval</c> (every few seconds) during ACME DNS-01 validation.</para>
        ///
        /// <para>271 bytes is well within safe <c>stackalloc</c> limits (the .NET runtime default stack size is 1 MiB on
        /// both Windows and Linux; this threshold is 0.026% of that).</para>
        /// </remarks>
        private const int MaxStackAllocQuerySize = DnsHeaderSize + MaxQnameLength + 4;

        /// <summary>
        /// Size of the QTYPE (2) + QCLASS (2) suffix appended after the QNAME in the question section.
        /// </summary>
        private const int QuestionSuffixSize = 4;

        #endregion

        #region Fields

        /// <summary>
        /// The authoritative nameserver IP addresses to query.  Immutable after construction.  When multiple IPs are
        /// provided, <see cref="QueryTxtAsync"/> selects one at random per call.
        /// </summary>
        private readonly IPAddress[] _nameservers;

        #endregion

        #region Properties

        /// <summary>
        /// The number of authoritative nameserver IP addresses this client is configured to query.  Exposed for diagnostic
        /// logging in <see cref="AcmeCertificateProvider.CreateAuthoritativeDnsClientAsync"/> when returning a cached
        /// client.
        /// </summary>
        internal int NameserverCount => _nameservers.Length;

        #endregion

        #region Constructors

        /// <summary>
        /// Initialises a new instance targeting the specified authoritative nameservers.
        /// </summary>
        /// <param name="nameservers">One or more authoritative nameserver IP addresses.  Must not be empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="nameservers"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="nameservers"/> is empty.</exception>
        internal LegacyAuthoritativeDnsClient(IPAddress[] nameservers)
        {
            ArgumentNullException.ThrowIfNull(nameservers);
            ArgumentOutOfRangeException.ThrowIfZero(nameservers.Length, nameof(nameservers));
            _nameservers = nameservers;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Sends a DNS TXT query for the specified record name to a randomly-selected authoritative nameserver and returns
        /// all TXT record values from the response.
        /// </summary>
        /// <remarks>
        /// <para><b>Query construction:</b> Builds a minimal DNS query packet: 12-byte header (randomised ID, no
        /// recursion, QDCOUNT=1) followed by the QNAME (label-encoded hostname), QTYPE=TXT (16), QCLASS=IN (1).
        /// The packet is built on the stack when the total size is ≤ <see cref="MaxStackAllocQuerySize"/> (271 bytes),
        /// which covers all valid single-question DNS queries.  The stack buffer is copied to a heap <c>byte[]</c> for
        /// <see cref="UdpClient.SendAsync"/> which requires a <c>ReadOnlyMemory{byte}</c>.</para>
        ///
        /// <para><b>Response parsing:</b> Skips the header and question section, then iterates the answer RRs.  For each
        /// RR with type TXT and class IN, the RDATA is parsed as a sequence of character-strings (RFC 1035 §3.3.14:
        /// length-prefixed segments) and concatenated into a single string -- matching the behaviour of the previous
        /// DnsClient implementation's <c>string.Concat(TxtRecord.Text)</c>.</para>
        ///
        /// <para><b>Name compression:</b> The parser handles DNS name compression pointers (RFC 1035 §4.1.4) in both the
        /// question and answer sections' NAME fields.  A pointer is identified by the two high bits being set
        /// (<c>0xC0</c>); the parser skips the 2-byte pointer rather than following the label chain.</para>
        ///
        /// <para><b>Cancellation and timeout:</b> A linked <see cref="CancellationTokenSource"/> combines the caller's
        /// <paramref name="ct"/> with a 5-second <see cref="CancellationTokenSource.CancelAfter(int)"/>.  If the timeout
        /// fires (but the caller's token is not cancelled), an empty list is returned so the caller's poll loop can retry.
        /// If the caller's token fires (host shutdown), the <see cref="OperationCanceledException"/> propagates
        /// normally.</para>
        ///
        /// <para><b>Socket exception handling:</b> <see cref="SocketException"/> from <see cref="UdpClient.SendAsync"/> or
        /// <see cref="UdpClient.ReceiveAsync(CancellationToken)"/> (e.g. ICMP port-unreachable translated to connection
        /// reset, network-unreachable) is caught and returns an empty list.  The caller's poll loop will retry on the next
        /// interval, potentially selecting a different nameserver.  <see cref="ObjectDisposedException"/> is also caught
        /// because <see cref="UdpClient"/> may throw it if the underlying socket is closed by the OS between the send and
        /// receive calls (observed on Linux under memory pressure).</para>
        /// </remarks>
        /// <param name="recordName">The fully-qualified DNS name to query (e.g. <c>_acme-challenge.example.com</c>).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A list of TXT record values (concatenated character-strings per RR).  Empty if no TXT records are
        /// found, the query times out, or a network error occurs.</returns>
        internal async Task<List<string>> QueryTxtAsync(string recordName, CancellationToken ct)
        {
            // Select a random nameserver for load distribution across anycast endpoints.
            IPAddress nameserver = _nameservers[Random.Shared.Next(_nameservers.Length)];

            byte[] queryPacket = BuildTxtQuery(recordName, out ushort queryId);

            using UdpClient udp = new(nameserver.AddressFamily);

            // Create a linked CTS that cancels on either host shutdown (ct) or our receive timeout.
            // UdpClient.ReceiveAsync accepts CancellationToken on .NET 8, so the linked CTS provides
            // both cooperative cancellation and timeout in a single token.
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ReceiveTimeoutMs);

            try
            {
                _ = await udp.SendAsync(queryPacket, new IPEndPoint(nameserver, 53), timeoutCts.Token).ConfigureAwait(false);

                UdpReceiveResult result = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
                return ParseTxtResponse(result.Buffer, queryId);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout (our CancelAfter fired), not host shutdown — return empty to let the caller's poll loop retry.
                return [];
            }
            catch (SocketException)
            {
                // Network error (ICMP port-unreachable, network-unreachable, connection reset, etc.) — return empty
                // to let the caller's poll loop retry, potentially selecting a different nameserver next time.
                return [];
            }
            catch (ObjectDisposedException)
            {
                // The UdpClient's underlying socket was closed externally (e.g. OS reclaimed the handle under memory
                // pressure on Linux, or a race between CancellationToken callback and the receive path).  Return empty
                // to let the caller's poll loop retry with a fresh socket.
                return [];
            }
        }

        #endregion

        #region Private Methods -- Query Construction

        /// <summary>
        /// Builds a DNS query packet for a TXT record lookup (RFC 1035 §4.1).
        /// </summary>
        /// <remarks>
        /// <para><b>Packet layout:</b></para>
        /// <code>
        ///   Header (12 bytes):
        ///     ID       = random 16-bit value
        ///     Flags    = 0x0000 (standard query, RD=0 -- authoritative servers don't need recursion)
        ///     QDCOUNT  = 1
        ///     ANCOUNT  = 0
        ///     NSCOUNT  = 0
        ///     ARCOUNT  = 0
        ///
        ///   Question:
        ///     QNAME    = label-encoded hostname (e.g. [16]"_acme-challenge"[7]"example"[3]"com"[0])
        ///     QTYPE    = 16 (TXT)
        ///     QCLASS   = 1  (IN)
        /// </code>
        ///
        /// <para><b>Input validation:</b> Each label is validated against the 63-byte maximum (RFC 1035 §2.3.4) and the
        /// total QNAME is validated against the 255-byte maximum (RFC 1035 §3.1).  Empty labels (from consecutive dots or
        /// leading/trailing dots) are rejected because they produce malformed wire-format queries that authoritative servers
        /// may handle unpredictably.  Label content is validated as pure ASCII via
        /// <see cref="EncodingUtilities.IsAscii(ReadOnlySpan{char})"/> to prevent silent character substitution in
        /// <see cref="Encoding.ASCII"/>.  Encoding is performed via <see cref="EncodingUtilities.AsciiToSpan"/> which
        /// provides a redundant fail-fast check -- if the <see cref="EncodingUtilities.IsAscii(ReadOnlySpan{char})"/>
        /// guard were ever removed, the encoding would still throw rather than silently corrupt the query.</para>
        ///
        /// <para><b>Stack allocation:</b> The query packet is assembled on the stack via <c>stackalloc</c> when the total
        /// size fits within <see cref="MaxStackAllocQuerySize"/> (271 bytes -- always true for valid DNS names).  The
        /// assembled packet is then copied to a heap <c>byte[]</c> because <see cref="UdpClient.SendAsync"/> requires a
        /// <see cref="ReadOnlyMemory{T}"/>.  The <c>stackalloc</c> avoids a separate heap allocation for the working
        /// buffer during packet assembly.</para>
        /// </remarks>
        /// <param name="name">The fully-qualified DNS name to query.</param>
        /// <param name="queryId">Receives the randomised 16-bit query ID for response matching.</param>
        /// <returns>The complete DNS query packet ready for UDP transmission.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty, contains empty labels, a
        /// label exceeds 63 bytes, the total QNAME exceeds 255 bytes, or a label contains non-ASCII
        /// characters.</exception>
        private static byte[] BuildTxtQuery(string name, out ushort queryId)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            queryId = (ushort)Random.Shared.Next(ushort.MaxValue + 1);

            // Calculate QNAME length: each label is 1 (length byte) + label.Length, plus 1 for the trailing 0x00.
            string[] labels = name.Split('.');
            int qnameLength = 1; // trailing 0x00
            foreach (string label in labels)
            {
                if (label.Length == 0)
                    throw new ArgumentException($"DNS name '{name}' contains an empty label (consecutive dots, leading dot, or trailing dot).", nameof(name));

                if (label.Length > MaxLabelLength)
                    throw new ArgumentException($"DNS label '{label}' exceeds the maximum length of {MaxLabelLength} bytes (RFC 1035 §2.3.4).", nameof(name));

                // Validate label content is pure ASCII before encoding.  Encoding.ASCII.GetBytes silently replaces
                // non-ASCII characters with '?' (0x3F), which would produce a malformed DNS query that silently
                // queries the wrong name.  Fail-fast with a descriptive exception instead.
                if (!EncodingUtilities.IsAscii(label.AsSpan()))
                    throw new ArgumentException($"DNS label '{label}' contains non-ASCII characters.  DNS names must be pure ASCII (RFC 1035 §2.3.4).", nameof(name));

                qnameLength += 1 + label.Length;
            }

            if (qnameLength > MaxQnameLength)
                throw new ArgumentException($"DNS name '{name}' exceeds the maximum QNAME length of {MaxQnameLength} bytes (RFC 1035 §3.1).", nameof(name));

            // Total: 12 (header) + qnameLength + 2 (QTYPE) + 2 (QCLASS).
            int packetLength = DnsHeaderSize + qnameLength + QuestionSuffixSize;

            // Assemble on the stack when possible (always true for valid DNS names -- max 271 bytes).
            // The final packet must be a heap byte[] for UdpClient.SendAsync (ReadOnlyMemory<byte>).
            Span<byte> span = packetLength <= MaxStackAllocQuerySize
                ? stackalloc byte[MaxStackAllocQuerySize]
                : new byte[packetLength];

            // Zero-initialise the header region.  stackalloc is zero-filled by the runtime on .NET 8,
            // but the heap path (new byte[]) is also zero-filled, so no explicit clear is needed.

            // Header.
            BinaryPrimitives.WriteUInt16BigEndian(span, queryId);      // ID
            // Flags: 0x0000 -- standard query, RD=0 (already zero-initialised).
            BinaryPrimitives.WriteUInt16BigEndian(span[4..], 1);       // QDCOUNT = 1
            // ANCOUNT, NSCOUNT, ARCOUNT = 0 (already zero-initialised).

            // Question -- QNAME (label-encoded).
            // Uses EncodingUtilities.AsciiToSpan instead of Encoding.ASCII.GetBytes for consistency with the
            // IsAscii validation above and to provide a redundant fail-fast safety net: if the IsAscii guard
            // were ever removed, AsciiToSpan would still throw ArgumentException on non-ASCII input rather
            // than silently substituting '?' (0x3F) bytes into the wire-format query.
            int offset = DnsHeaderSize;
            foreach (string label in labels)
            {
                span[offset++] = (byte)label.Length;
                offset += EncodingUtilities.AsciiToSpan(label, span[offset..]);
            }
            span[offset++] = 0; // Root label terminator.

            // QTYPE = TXT (16), QCLASS = IN (1).
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], DnsTypeTxt);
            offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], DnsClassIn);

            // Copy from the (possibly stack-allocated) working buffer to a heap byte[] for SendAsync.
            return span[..packetLength].ToArray();
        }

        #endregion

        #region Private Methods -- Response Parsing

        /// <summary>
        /// Parses TXT record values from a DNS response packet.
        /// </summary>
        /// <remarks>
        /// <para><b>Validation:</b> The flags word is checked for four conditions before any answer parsing begins:</para>
        /// <list type="number">
        ///   <item><description><b>QR bit</b> (bit 15): must be 1 (response).  Rejects stray query packets that arrive on
        ///     the same socket -- e.g. from a spoofed source or a loopback misconfiguration.</description></item>
        ///   <item><description><b>OPCODE</b> (bits 14-11): must be 0 (QUERY).  A non-zero OPCODE (IQUERY, STATUS, or
        ///     reserved values) indicates a response to a different operation type -- not a standard query
        ///     response.</description></item>
        ///   <item><description><b>TC bit</b> (bit 9): must be 0 (not truncated).  A truncated response has an incomplete
        ///     answer section -- parsing it would yield partial or missing TXT records.  For ACME challenge TXT values
        ///     (43 bytes) this will essentially never trigger, but rejecting truncated responses is technically correct and
        ///     lets the caller's poll loop retry on the next interval.</description></item>
        ///   <item><description><b>RCODE</b> (bits 3-0): must be 0 (NOERROR).  Any non-zero RCODE (NXDOMAIN, SERVFAIL,
        ///     REFUSED, etc.) means the server could not answer the query.</description></item>
        /// </list>
        /// <para>The response ID is also checked against the query ID to reject mismatched or spoofed replies.</para>
        ///
        /// <para><b>Combined flags mask:</b> The four flag checks (QR, OPCODE, TC, RCODE) are combined into a single
        /// bitmask comparison: <c>(flags &amp; 0xFA0F) == 0x8000</c>.  The mask <c>0xFA0F</c> selects QR (0x8000),
        /// OPCODE (0x7800), TC (0x0200), and RCODE (0x000F).  The expected value <c>0x8000</c> means QR=1 and all other
        /// masked bits zero.  This replaces four separate bitwise checks with a single comparison -- functionally
        /// identical but eliminates three conditional branches on the hot path.</para>
        ///
        /// <para><b>Question skip:</b> The question section is skipped by walking the QNAME labels (each preceded by a
        /// length byte, terminated by 0x00) plus 4 bytes for QTYPE + QCLASS.  Compression pointers are handled.</para>
        ///
        /// <para><b>Answer parsing:</b> Each answer RR is parsed as: NAME (label or compression pointer), TYPE (2),
        /// CLASS (2), TTL (4), RDLENGTH (2), RDATA (RDLENGTH bytes).  For TXT RRs, RDATA contains one or more
        /// character-strings, each a length-prefixed byte sequence (RFC 1035 §3.3.14).  All character-strings in a
        /// single RR are concatenated into one result string -- matching the previous DnsClient implementation's
        /// <c>string.Concat(TxtRecord.Text)</c> behaviour.</para>
        ///
        /// <para><b>Single-segment optimisation:</b> ACME challenge TXT records contain exactly one 43-byte
        /// character-string.  When a TXT RR contains a single character-string that spans the entire RDATA (the common
        /// case), the string is created directly from the span without allocating a <see cref="StringBuilder"/>.  The
        /// <see cref="StringBuilder"/> path is retained for multi-segment TXT RRs (e.g. DKIM, SPF) to maintain
        /// correctness.</para>
        ///
        /// <para><b>Robustness:</b> All offset advances are bounds-checked against the buffer length.  Malformed
        /// responses (truncated, invalid compression pointers, short RDATA) cause an early return with whatever TXT
        /// values were successfully parsed, rather than throwing.  This is appropriate because the caller retries on
        /// the next poll interval.</para>
        /// </remarks>
        /// <param name="buffer">The raw UDP response bytes.</param>
        /// <param name="expectedId">The query ID to match against the response.</param>
        /// <returns>A list of TXT record values.  Empty if validation fails or no TXT answers are present.</returns>
        private static List<string> ParseTxtResponse(byte[] buffer, ushort expectedId)
        {
            List<string> results = [];

            if (buffer.Length < DnsHeaderSize)
                return results;

            ReadOnlySpan<byte> span = buffer;

            // Validate response ID matches our query.
            ushort responseId = BinaryPrimitives.ReadUInt16BigEndian(span);
            if (responseId != expectedId)
                return results;

            // Validate flags with a single combined bitmask comparison.
            //
            // Mask 0xFA0F selects:
            //   Bit 15    (0x8000): QR     -- 0 = query, 1 = response.
            //   Bits 14-11(0x7800): OPCODE -- 0 = QUERY (standard query).
            //   Bit  9    (0x0200): TC     -- 1 = message was truncated (answer section incomplete).
            //   Bits 3-0  (0x000F): RCODE  -- 0 = NOERROR.
            //
            // Expected: 0x8000 -- QR=1 (response), all others zero (OPCODE=QUERY, not truncated, NOERROR).
            //
            // This replaces four separate bitwise checks with one comparison -- functionally identical
            // but eliminates three conditional branches.
            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
            if ((flags & 0xFA0F) != 0x8000)
                return results;

            ushort qdCount = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            ushort anCount = BinaryPrimitives.ReadUInt16BigEndian(span[6..]);

            int offset = DnsHeaderSize;

            // Skip the question section.
            for (int q = 0; q < qdCount; q++)
            {
                if (!TrySkipName(span, ref offset))
                    return results;

                // QTYPE (2) + QCLASS (2) = 4 bytes following the QNAME.
                if (offset + QuestionSuffixSize > span.Length)
                    return results;

                offset += QuestionSuffixSize;
            }

            // Parse answer RRs.
            for (int a = 0; a < anCount; a++)
            {
                if (!TrySkipName(span, ref offset))
                    return results;

                // TYPE (2) + CLASS (2) + TTL (4) + RDLENGTH (2) = 10 bytes.
                if (offset + RrFixedFieldsSize > span.Length)
                    return results;

                ushort rrType = BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);
                ushort rrClass = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 2)..]);
                // TTL at offset + 4 (4 bytes) -- not needed.
                ushort rdLength = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 8)..]);
                offset += RrFixedFieldsSize;

                if (offset + rdLength > span.Length)
                    return results;

                if (rrType == DnsTypeTxt && rrClass == DnsClassIn)
                {
                    string? txtValue = ParseTxtRdata(span, ref offset, rdLength);
                    if (txtValue is not null)
                        results.Add(txtValue);
                }
                else
                {
                    // Skip RDATA for non-TXT records.
                    offset += rdLength;
                }
            }

            return results;
        }

        /// <summary>
        /// Parses the RDATA of a single TXT resource record into a concatenated string of all character-strings.
        /// </summary>
        /// <remarks>
        /// <para>TXT RDATA consists of one or more character-strings (RFC 1035 §3.3.14), each a single length byte
        /// followed by that many bytes of text.  All character-strings in the RR are concatenated into one result
        /// string.</para>
        ///
        /// <para><b>Single-segment fast path:</b> When the first character-string spans the entire RDATA (length byte
        /// equals <c>rdLength - 1</c>), the string is created directly via
        /// <see cref="Encoding.ASCII"/>.<see cref="Encoding.GetString(byte[], int, int)">GetString</see> without
        /// allocating a <see cref="StringBuilder"/>.  This covers 100% of ACME challenge TXT records (a single 43-byte
        /// base64url value) and avoids the <see cref="StringBuilder"/> allocation + copy overhead on the hot
        /// path.</para>
        ///
        /// <para><b>Multi-segment fallback:</b> If the first character-string does not span the entire RDATA, a
        /// <see cref="StringBuilder"/> is used to concatenate all remaining segments.  This handles multi-segment TXT
        /// records (e.g. DKIM keys, SPF records split across 255-byte segments) correctly, though such records are not
        /// expected in the ACME challenge use case.</para>
        ///
        /// <para><b>Offset alignment guarantee:</b> On all exit paths (success, malformed data, short buffer), the
        /// <paramref name="offset"/> is advanced to <c>rdEnd</c> to keep subsequent RR parsing aligned.  Without this
        /// guarantee, a malformed TXT RDATA would leave the offset mid-RDATA, causing all subsequent RR parsing to
        /// read garbage.</para>
        ///
        /// <para><b>ASCII decoding of untrusted data:</b> <see cref="Encoding.ASCII"/>.<see cref="Encoding.GetString(byte[], int, int)">GetString</see>
        /// is used rather than <see cref="EncodingUtilities"/> because the response data comes from an untrusted
        /// network source.  <see cref="Encoding.ASCII"/> silently replaces non-ASCII bytes (≥ 0x80) with <c>'?'</c>
        /// (0x3F) -- this is acceptable here because: (1) ACME challenge TXT values are base64url-encoded (a subset
        /// of ASCII), so non-ASCII bytes indicate a corrupted or spoofed response; (2) the caller
        /// (<see cref="AcmeCertificateProvider.WaitForTxtRecordAsync"/>) performs an exact string comparison against
        /// the expected challenge value, so a corrupted decode simply fails the match and the poll loop retries; and
        /// (3) throwing on non-ASCII response data would be counterproductive -- it would prevent graceful retry and
        /// could be triggered by an adversarial nameserver response.</para>
        /// </remarks>
        /// <param name="span">The full DNS response buffer.</param>
        /// <param name="offset">Current read position; advanced past the RDATA on return.</param>
        /// <param name="rdLength">The RDATA length from the RR header.</param>
        /// <returns>The concatenated TXT value, or <see langword="null"/> if the RDATA is malformed or empty.</returns>
        private static string? ParseTxtRdata(ReadOnlySpan<byte> span, ref int offset, ushort rdLength)
        {
            int rdEnd = offset + rdLength;

            if (offset >= rdEnd || offset >= span.Length)
            {
                offset = rdEnd;
                return null;
            }

            // Read the first character-string length.
            int firstStrLen = span[offset++];
            if (offset + firstStrLen > span.Length || offset + firstStrLen > rdEnd)
            {
                offset = rdEnd; // Advance past malformed RDATA to keep subsequent RR parsing aligned.
                return null;
            }

            // Fast path: single character-string spans the entire RDATA (length byte + content = rdLength).
            // This covers all ACME challenge TXT records (one 43-byte segment) without StringBuilder allocation.
            if (offset + firstStrLen == rdEnd)
            {
                string result = Encoding.ASCII.GetString(span.Slice(offset, firstStrLen));
                offset = rdEnd;
                return result;
            }

            // Multi-segment fallback: concatenate all character-strings via StringBuilder.
            StringBuilder sb = new(rdLength); // Hint capacity with rdLength -- close enough for total text bytes.
            _ = sb.Append(Encoding.ASCII.GetString(span.Slice(offset, firstStrLen)));
            offset += firstStrLen;

            while (offset < rdEnd)
            {
                if (offset >= span.Length)
                    break;

                int strLen = span[offset++];
                if (offset + strLen > span.Length || offset + strLen > rdEnd)
                    break;

                _ = sb.Append(Encoding.ASCII.GetString(span.Slice(offset, strLen)));
                offset += strLen;
            }

            offset = rdEnd; // Ensure offset is aligned to the end of RDATA even if parsing ended early.
            return sb.ToString();
        }

        #endregion

        #region Private Methods -- Name Traversal

        /// <summary>
        /// Advances <paramref name="offset"/> past a DNS name in the wire format, handling both inline labels and
        /// compression pointers (RFC 1035 §4.1.4).
        /// </summary>
        /// <remarks>
        /// <para>An inline label starts with a length byte (0-63); a compression pointer starts with the two high bits set
        /// (<c>0xC0</c>) followed by a 14-bit offset into the message.  This method does not follow pointers -- it only
        /// needs to skip past the NAME field to reach the subsequent RR fields.</para>
        ///
        /// <para><b>Loop safety:</b> A hop counter (<see cref="MaxCompressionPointerHops"/>) prevents infinite loops from
        /// adversarial packets containing circular compression pointer chains.  A legitimate DNS name has at most 127
        /// labels, so the limit is never reached for valid responses.</para>
        ///
        /// <para><b>Reserved label type guard:</b> Label bytes with the two high bits set to <c>0x40</c> or <c>0x80</c> are
        /// reserved by RFC 1035 §4.1.4 and are not valid label lengths or compression pointers.  If encountered, the method
        /// returns <see langword="false"/> to reject the malformed name rather than misinterpreting the byte as a label
        /// length (which could advance the offset far beyond the actual name boundary).</para>
        /// </remarks>
        /// <param name="span">The full DNS response buffer.</param>
        /// <param name="offset">Current read position; advanced past the name on return.</param>
        /// <returns><see langword="true"/> if the name was successfully skipped; <see langword="false"/> if the buffer is
        /// too short, the hop limit is exceeded, or a reserved label type is encountered.</returns>
        private static bool TrySkipName(ReadOnlySpan<byte> span, ref int offset)
        {
            int hops = 0;

            while (offset < span.Length)
            {
                if (++hops > MaxCompressionPointerHops)
                    return false; // Circular compression pointer chain -- reject to prevent infinite loop.

                byte b = span[offset];

                if (b == 0)
                {
                    // Root label -- end of name.
                    offset++;
                    return true;
                }

                if ((b & 0xC0) == 0xC0)
                {
                    // Compression pointer -- 2 bytes total.  Don't follow it; just skip past.
                    if (offset + 2 > span.Length)
                        return false;

                    offset += 2;
                    return true;
                }

                // Guard against reserved label types (high two bits = 0x40 or 0x80).  RFC 1035 §4.1.4 reserves
                // these patterns; treating them as label lengths would advance the offset incorrectly.
                if ((b & 0xC0) != 0)
                    return false;

                // Inline label: 1 length byte + label bytes.
                int advance = 1 + b;
                if (offset + advance > span.Length)
                    return false;

                offset += advance;
            }

            return false;
        }

        #endregion
    }

}
