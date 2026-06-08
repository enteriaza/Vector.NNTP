// <copyright file="SpamdScanArticleBuilder.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH (Tier 2): temporary RFC5322-ish article copy for spamd CHECK on eligible articles; original spool bytes unchanged.

using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Vector.NNTP.Articles.Scanning;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Builds a temporary spamd scan article with programmatic NNTP-flavored headers while preserving the original body.
    /// </summary>
    /// <remarks>
    /// <para><b>Tier:</b> Invoked only for non-yEnc articles under the spam size gate in
    /// <see cref="ArticleSpoolPostprocessor"/> — not on every ingested article.</para>
    /// <para>
    /// Mutations apply only to the scan copy returned by <see cref="BuildScanArticle"/>. Spool writes continue to use
    /// the original article bytes from the transit queue.
    /// </para>
    /// <para>
    /// Header scanning uses <see cref="ArticleByteScanSimd"/>; output is assembled with
    /// <see cref="ArrayBufferWriter{T}"/> to avoid <see cref="MemoryStream"/> overhead.
    /// </para>
    /// <para>
    /// <b>Output header order:</b> synthetic <c>Received:</c> and <c>To:</c>; optional
    /// <c>X-Usenet-Newsgroups:</c> when the original carried <c>Newsgroups:</c>; synthetic <c>Date:</c> only when the
    /// original lacked one; then preserved original headers (including <c>Newsgroups:</c> when present); then
    /// <c>\r\n</c> and the unmodified body slice.
    /// </para>
    /// <para><b>Threading:</b> Instance carries no mutable state; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal sealed class SpamdScanArticleBuilder
    {
        /// <summary>
        /// Lowercase ASCII literals for operational NNTP headers stripped from the spamd scan copy.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Matched case-insensitively via <see cref="IsRemovedHeaderName"/> on raw name bytes and via
        /// <see cref="IsRemovedHeader"/> after UTF-8 decode. Fields include transit tracing and posting metadata that
        /// spamd does not need and that would duplicate synthesized <c>Received:</c> context.
        /// </para>
        /// </remarks>
        private static readonly string[] RemovedHeaderNames =
        [
            "xref",
            "injection-info",
            "x-trace",
            "x-complaints-to",
            "nntp-posting-host",
            "path",
        ];

        /// <summary>
        /// Builds a spamd scan article with synthetic <c>Received:</c> and <c>To:</c> headers.
        /// </summary>
        /// <param name="originalArticleBytes">Original preprocessed article bytes (headers plus body).</param>
        /// <param name="origin">
        /// Peer address, optional resolved host name, and UTC reception timestamp from
        /// <see cref="NntpSpoolWriteItem.Origin"/> for honest <c>Received:</c> synthesis.
        /// </param>
        /// <param name="serverOptions">
        /// Local server identity from <see cref="Sockets.Configuration.NntpServerIdentityExtensions.GetServerReceivedByClause"/> and
        /// <see cref="Sockets.Configuration.NntpServerIdentityExtensions.GetSpamScanToAddress"/>.
        /// </param>
        /// <param name="messageId">
        /// Validated transit Message-ID for the article, included verbatim as the <c>id</c> token in the synthetic
        /// <c>Received:</c> header. May be bracketed or unbracketed; <see cref="NormalizeReceivedMessageId"/> normalizes
        /// it to the <c>&lt;token&gt;</c> form.
        /// </param>
        /// <returns>
        /// A newly allocated byte array containing rewritten headers and the identical original body octets starting at
        /// the resolved body offset.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="serverOptions"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <see cref="ArticleByteScanSimd.FindHeaderEnd"/> cannot locate a header/body separator.
        /// </exception>
        /// <remarks>
        /// <para>
        /// When <see cref="ArticleByteScanSimd.FindBodyStart"/> returns <c>-1</c>, advances past contiguous
        /// <c>\r</c>/<c>\n</c> bytes after the header boundary index to locate the first body octet.
        /// </para>
        /// <para>
        /// Operational headers listed in <see cref="RemovedHeaderNames"/> are omitted from the preserved set. A
        /// present <c>Newsgroups:</c> field is copied to <c>X-Usenet-Newsgroups:</c> and may also remain in the
        /// preserved header list under its original name.
        /// </para>
        /// <para>
        /// The synthetic <c>Received:</c> field uses the full four-clause form:
        /// <c>from … by {fqdn} ({ident}) with NNTP id {msgid}; {date}</c>.
        /// The <c>by</c> clause includes <see cref="NntpServerOptions.ServerIdentification"/> in parentheses when set,
        /// matching the same identification string exposed in the NNTP greeting and <c>CAPABILITIES IMPLEMENTATION</c>.
        /// </para>
        /// </remarks>
        public byte[] BuildScanArticle(
            ReadOnlySpan<byte> originalArticleBytes,
            NntpSpoolArticleOrigin origin,
            NntpServerOptions serverOptions,
            string messageId)
        {
            ArgumentNullException.ThrowIfNull(serverOptions);

            int headerEnd = ArticleByteScanSimd.FindHeaderEnd(originalArticleBytes);
            if (headerEnd < 0)
            {
                throw new InvalidOperationException("Article header terminator was not found.");
            }

            int bodyStart = ArticleByteScanSimd.FindBodyStart(originalArticleBytes);
            if (bodyStart < 0)
            {
                bodyStart = headerEnd;
                while (bodyStart < originalArticleBytes.Length &&
                       originalArticleBytes[bodyStart] is (byte)'\r' or (byte)'\n')
                {
                    bodyStart++;
                }
            }

            var preservedHeaders = new List<(string Name, string Value)>(16);
            string? newsgroupsValue = null;
            bool hasDate = false;

            ParseHeaders(originalArticleBytes[..headerEnd], preservedHeaders, ref newsgroupsValue, ref hasDate);

            var output = new ArrayBufferWriter<byte>(originalArticleBytes.Length + 512);
            WriteHeader(output, "Received", BuildReceivedHeader(origin, serverOptions, messageId));
            WriteHeader(output, "To", serverOptions.GetSpamScanToAddress());

            if (newsgroupsValue is not null)
            {
                WriteHeader(output, "X-Usenet-Newsgroups", newsgroupsValue);
            }

            if (!hasDate)
            {
                WriteHeader(output, "Date", FormatMailDate(DateTimeOffset.UtcNow));
            }

            foreach ((string name, string value) in preservedHeaders)
            {
                WriteHeader(output, name, value);
            }

            AppendBytes(output, "\r\n"u8);
            if (bodyStart < originalArticleBytes.Length)
            {
                AppendBytes(output, originalArticleBytes[bodyStart..]);
            }

            return output.WrittenSpan.ToArray();
        }

        /// <summary>
        /// Parses original headers, preserving non-operational fields and collecting values needed for synthesis.
        /// </summary>
        /// <param name="headerBytes">Header section bytes without the terminating blank line.</param>
        /// <param name="preservedHeaders">
        /// Output list populated with decoded name/value pairs that are not in <see cref="RemovedHeaderNames"/>.
        /// </param>
        /// <param name="newsgroupsValue">
        /// Set to the unfolded <c>Newsgroups:</c> value when that field appears; unchanged when absent.
        /// </param>
        /// <param name="hasDate">
        /// Set to <see langword="true"/> when a <c>Date:</c> header is committed; otherwise left unchanged.
        /// </param>
        /// <remarks>
        /// <para>
        /// Iterates header lines using <see cref="ArticleByteScanSimd.IndexOfLineFeed"/>. Continuation lines (leading
        /// space or tab) are unfolded into the current field with embedded line feeds. Lines without a colon before the
        /// first non-whitespace byte are skipped without failing the scan build.
        /// </para>
        /// <para>
        /// Removed header names are detected on ASCII name bytes via <see cref="IsRemovedHeaderName"/> before UTF-8
        /// value decoding to avoid allocating strings for stripped fields.
        /// </para>
        /// </remarks>
        private static void ParseHeaders(
            ReadOnlySpan<byte> headerBytes,
            List<(string Name, string Value)> preservedHeaders,
            ref string? newsgroupsValue,
            ref bool hasDate)
        {
            int index = 0;
            string? currentName = null;
            var currentValue = new StringBuilder();

            while (index < headerBytes.Length)
            {
                int lineEnd = ArticleByteScanSimd.IndexOfLineFeed(headerBytes, index, headerBytes.Length);

                int contentEnd = lineEnd;
                if (contentEnd > index && headerBytes[contentEnd - 1] == (byte)'\r')
                {
                    contentEnd--;
                }

                ReadOnlySpan<byte> line = headerBytes.Slice(index, contentEnd - index);
                if (line.Length == 0)
                {
                    break;
                }

                if (line[0] is (byte)' ' or (byte)'\t')
                {
                    if (currentName is not null)
                    {
                        if (currentValue.Length > 0)
                        {
                            currentValue.Append('\n');
                        }

                        currentValue.Append(Encoding.UTF8.GetString(line));
                    }

                    index = lineEnd + 1;
                    continue;
                }

                if (currentName is not null)
                {
                    CommitHeader(currentName, currentValue.ToString(), preservedHeaders, ref newsgroupsValue, ref hasDate);
                    currentValue.Clear();
                }

                int colon = line.IndexOf((byte)':');
                if (colon <= 0)
                {
                    index = lineEnd + 1;
                    currentName = null;
                    continue;
                }

                ReadOnlySpan<byte> nameBytes = line[..colon];
                if (IsRemovedHeaderName(nameBytes))
                {
                    currentName = null;
                    index = lineEnd + 1;
                    continue;
                }

                currentName = Encoding.UTF8.GetString(nameBytes).Trim();
                ReadOnlySpan<byte> valueBytes = line[(colon + 1)..];
                if (valueBytes.Length > 0 && valueBytes[0] == (byte)' ')
                {
                    valueBytes = valueBytes[1..];
                }

                currentValue.Clear();
                currentValue.Append(Encoding.UTF8.GetString(valueBytes));
                index = lineEnd + 1;
            }

            if (currentName is not null)
            {
                CommitHeader(currentName, currentValue.ToString(), preservedHeaders, ref newsgroupsValue, ref hasDate);
            }
        }

        /// <summary>
        /// Commits one parsed header into preserved output or synthesis side channels.
        /// </summary>
        /// <param name="name">Decoded header field name.</param>
        /// <param name="value">Unfolded header field value.</param>
        /// <param name="preservedHeaders">
        /// Output list; receives <paramref name="name"/> and <paramref name="value"/> unless the name is removed.
        /// </param>
        /// <param name="newsgroupsValue">
        /// Updated when <paramref name="name"/> is <c>newsgroups</c> (case-insensitive); otherwise unchanged.
        /// </param>
        /// <param name="hasDate">
        /// Set to <see langword="true"/> when <paramref name="name"/> is <c>date</c> (case-insensitive).
        /// </param>
        /// <remarks>
        /// <para>
        /// Uses <see cref="string.ToLowerInvariant"/> once per committed field to match
        /// <see cref="RemovedHeaderNames"/> with ordinal equality. Fields already stripped at the byte layer in
        /// <see cref="ParseHeaders"/> should not reach this method.
        /// </para>
        /// </remarks>
        private static void CommitHeader(
            string name,
            string value,
            List<(string Name, string Value)> preservedHeaders,
            ref string? newsgroupsValue,
            ref bool hasDate)
        {
            string lower = name.ToLowerInvariant();
            if (IsRemovedHeader(lower))
            {
                return;
            }

            if (lower == "newsgroups")
            {
                newsgroupsValue = value;
            }

            if (lower == "date")
            {
                hasDate = true;
            }

            preservedHeaders.Add((name, value));
        }

        /// <summary>
        /// Returns whether raw header name bytes match a removed field before UTF-8 string materialization.
        /// </summary>
        /// <param name="nameBytes">Header name bytes before the colon (may include surrounding whitespace).</param>
        /// <returns>
        /// <see langword="true"/> when the trimmed name equals a member of <see cref="RemovedHeaderNames"/> under ASCII
        /// case-insensitive comparison.
        /// </returns>
        /// <remarks>
        /// Compares against the same literals as <see cref="IsRemovedHeader"/> but on raw bytes so UTF-8 name
        /// materialization is skipped for stripped fields.
        /// </remarks>
        private static bool IsRemovedHeaderName(ReadOnlySpan<byte> nameBytes)
        {
            ReadOnlySpan<byte> trimmed = TrimHeaderFieldName(nameBytes);
            return MatchesRemovedName(trimmed, "xref"u8)
                || MatchesRemovedName(trimmed, "injection-info"u8)
                || MatchesRemovedName(trimmed, "x-trace"u8)
                || MatchesRemovedName(trimmed, "x-complaints-to"u8)
                || MatchesRemovedName(trimmed, "nntp-posting-host"u8)
                || MatchesRemovedName(trimmed, "path"u8);
        }

        /// <summary>
        /// Tests exact-length ASCII case-insensitive equality against a removed header literal.
        /// </summary>
        /// <param name="name">Trimmed header name bytes.</param>
        /// <param name="literal">Lowercase removed header literal.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="name"/> and <paramref name="literal"/> have equal length and
        /// match under <see cref="ArticleByteScanSimd.StartsWithAsciiIgnoreCase"/>.
        /// </returns>
        /// <remarks>
        /// Equal-length guard ensures the match is exact rather than a prefix of a longer field name.
        /// </remarks>
        private static bool MatchesRemovedName(ReadOnlySpan<byte> name, ReadOnlySpan<byte> literal)
        {
            return name.Length == literal.Length &&
                   ArticleByteScanSimd.StartsWithAsciiIgnoreCase(name, literal);
        }

        /// <summary>
        /// Trims ASCII horizontal whitespace from a header field name span.
        /// </summary>
        /// <param name="nameBytes">Candidate name bytes.</param>
        /// <returns>Slice of <paramref name="nameBytes"/> without leading or trailing space/tab bytes.</returns>
        /// <remarks>Does not trim other Unicode whitespace; header names are expected to use ASCII WSP only.</remarks>
        private static ReadOnlySpan<byte> TrimHeaderFieldName(ReadOnlySpan<byte> nameBytes)
        {
            int start = 0;
            while (start < nameBytes.Length && nameBytes[start] is (byte)' ' or (byte)'\t')
            {
                start++;
            }

            int end = nameBytes.Length;
            while (end > start && nameBytes[end - 1] is (byte)' ' or (byte)'\t')
            {
                end--;
            }

            return nameBytes[start..end];
        }

        /// <summary>
        /// Returns whether a lowercase header name is stripped from the scan copy.
        /// </summary>
        /// <param name="lowerName">Lowercase header field name.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="lowerName"/> equals an entry in
        /// <see cref="RemovedHeaderNames"/> using <see cref="StringComparison.Ordinal"/>.
        /// </returns>
        /// <remarks>
        /// Secondary guard after <see cref="IsRemovedHeaderName"/> for fields decoded before the byte fast path runs
        /// or when commit paths receive already-materialized names.
        /// </remarks>
        private static bool IsRemovedHeader(string lowerName)
        {
            foreach (string removed in RemovedHeaderNames)
            {
                if (string.Equals(lowerName, removed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds a single honest NNTP-flavored <c>Received:</c> header value.
        /// </summary>
        /// <param name="origin">Peer and reception metadata.</param>
        /// <param name="serverOptions">Local server identity.</param>
        /// <param name="messageId">
        /// Validated transit Message-ID placed in the <c>id</c> clause. Normalized to <c>&lt;token&gt;</c> form via
        /// <see cref="NormalizeReceivedMessageId"/>.
        /// </param>
        /// <returns>
        /// Folded <c>Received:</c> field body using CRLF continuations. When
        /// <see cref="NntpSpoolArticleOrigin.PeerHostName"/> is present, emits
        /// <c>from host (host [ip])</c>; otherwise <c>from [ip]</c> (IPv6 bracketed via <see cref="FormatPeerIp"/>).
        /// The <c>by</c> clause uses <see cref="NntpServerIdentityExtensions.GetServerReceivedByClause"/> to include
        /// the optional server identification in parentheses. The <c>id</c> clause carries the normalized Message-ID
        /// followed by a semicolon; the date is on its own folded line.
        /// </returns>
        /// <remarks>
        /// Reception time comes from <see cref="NntpSpoolArticleOrigin.ReceivedUtc"/>; local identity from
        /// <paramref name="serverOptions"/>. Never throws.
        /// </remarks>
        private static string BuildReceivedHeader(NntpSpoolArticleOrigin origin, NntpServerOptions serverOptions, string messageId)
        {
            string byClause = serverOptions.GetServerReceivedByClause();
            string receptionDate = FormatMailDate(origin.ReceivedUtc);
            string peerIp = FormatPeerIp(origin.PeerAddress);
            string normalizedId = NormalizeReceivedMessageId(messageId);

            if (!string.IsNullOrWhiteSpace(origin.PeerHostName))
            {
                string host = origin.PeerHostName.Trim();
                return $"from {host} ({host} [{peerIp}])\r\n    by {byClause}\r\n    with NNTP\r\n    id {normalizedId};\r\n    {receptionDate}";
            }

            return $"from [{peerIp}]\r\n    by {byClause}\r\n    with NNTP\r\n    id {normalizedId};\r\n    {receptionDate}";
        }

        /// <summary>
        /// Normalizes a transit Message-ID to the <c>&lt;token&gt;</c> form required by the <c>Received:</c> <c>id</c> clause.
        /// </summary>
        /// <param name="messageId">Raw Message-ID, which may or may not carry outer angle brackets.</param>
        /// <returns>
        /// The Message-ID wrapped in a single pair of angle brackets with surrounding whitespace removed, for example
        /// <c>&lt;scan@example.com&gt;</c>.
        /// </returns>
        /// <remarks>
        /// If the input is already bracketed, the existing brackets are stripped and a fresh pair is applied so the
        /// output never contains doubled brackets. An empty or whitespace input returns an empty string.
        /// </remarks>
        private static string NormalizeReceivedMessageId(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return string.Empty;
            }

            ReadOnlySpan<char> span = messageId.AsSpan().Trim();
            if (span.Length >= 2 && span[0] == '<' && span[^1] == '>')
            {
                span = span[1..^1].Trim();
            }

            return string.Create(CultureInfo.InvariantCulture, $"<{span}>");
        }

        /// <summary>
        /// Formats a peer IP for mail-style <c>Received:</c> clauses (brackets for IPv6).
        /// </summary>
        /// <param name="address">Peer address.</param>
        /// <returns>
        /// Dotted IPv4 text, or bracketed IPv6 text (for example <c>[2001:db8::1]</c>) suitable for mail-style clauses.
        /// </returns>
        /// <remarks>Never throws; assumes <paramref name="address"/> is a valid <see cref="IPAddress"/> instance.</remarks>
        private static string FormatPeerIp(IPAddress address)
        {
            return address.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{address}]"
                : address.ToString();
        }

        /// <summary>
        /// Formats a UTC timestamp in RFC 5322 mail date form.
        /// </summary>
        /// <param name="timestamp">UTC reception instant.</param>
        /// <returns>
        /// Mail date string in invariant culture with a fixed <c>+0000</c> offset (for example
        /// <c>Sun, 07 Jun 2026 18:42:17 +0000</c>).
        /// </returns>
        /// <remarks>
        /// Converts <paramref name="timestamp"/> to UTC before formatting. Used for synthetic
        /// <c>Date:</c> headers and <c>Received:</c> clauses.
        /// </remarks>
        private static string FormatMailDate(DateTimeOffset timestamp)
        {
            return timestamp.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss +0000", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Writes one header field with CRLF line endings into a buffer writer.
        /// </summary>
        /// <param name="output">Output buffer writer.</param>
        /// <param name="name">Header field name.</param>
        /// <param name="value">
        /// Header field value. May contain embedded <c>\r\n</c> pairs for folded fields such as
        /// <see cref="BuildReceivedHeader"/> output.
        /// </param>
        /// <remarks>
        /// Encodes <c>{name}: {value}\r\n</c> as UTF-8 into <paramref name="output"/> using
        /// <see cref="Encoding.GetMaxByteCount(int)"/> to size the writer span. Does not fold long lines.
        /// </remarks>
        private static void WriteHeader(ArrayBufferWriter<byte> output, string name, string value)
        {
            string line = $"{name}: {value}\r\n";
            int maxByteCount = Encoding.UTF8.GetMaxByteCount(line.Length);
            Span<byte> span = output.GetSpan(maxByteCount);
            int written = Encoding.UTF8.GetBytes(line, span);
            output.Advance(written);
        }

        /// <summary>
        /// Appends raw bytes to a buffer writer without encoding conversion.
        /// </summary>
        /// <param name="output">Output buffer writer receiving the copy.</param>
        /// <param name="bytes">Bytes to append (for example header/body separator or body slice).</param>
        /// <remarks>
        /// Used for the blank line before the body and for copying the original body span verbatim into the scan copy.
        /// </remarks>
        private static void AppendBytes(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> bytes)
        {
            Span<byte> span = output.GetSpan(bytes.Length);
            bytes.CopyTo(span);
            output.Advance(bytes.Length);
        }
    }
}
