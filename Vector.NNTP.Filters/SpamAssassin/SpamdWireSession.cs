// <copyright file="SpamdWireSession.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: one-shot TCP session per spamd request; shuts down send after request per PROTOCOL.
// SpamdWireSession.cs -- Low-level spamc/spamd framing, header IO, and response parsing.

using System.Buffers;
using System.Globalization;

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Executes a single spamd request/response exchange over TCP.
    /// </summary>
    internal sealed class SpamdWireSession : IAsyncDisposable
    {
        /// <summary>Maximum spamd response body size (64 MiB) to bound memory use.</summary>
        private const int MaxResponseBodyBytes = 64 * 1024 * 1024;

        /// <summary>Initial read buffer capacity for header and small bodies.</summary>
        private const int InitialReadBufferSize = 8192;

        /// <summary>Regex matching the <c>Spam:</c> response header value.</summary>
        private static readonly Regex SpamHeaderRegex = new(
            @"^\s*(True|False)\s*;\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>TCP client for the spamd connection.</summary>
        private readonly TcpClient _tcpClient;

        /// <summary>Network stream used for bidirectional I/O until disposed.</summary>
        private readonly NetworkStream _stream;

        /// <summary>Options snapshot for this session.</summary>
        private readonly SpamAssassinOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpamdWireSession"/> class (connection completed by <see cref="ConnectAsync"/>).
        /// </summary>
        /// <param name="options">Host, port, and timeout settings.</param>
        /// <param name="tcpClient">Connected TCP client.</param>
        /// <param name="stream">Network stream for the connected client.</param>
        private SpamdWireSession(SpamAssassinOptions options, TcpClient tcpClient, NetworkStream stream)
        {
            ArgumentNullException.ThrowIfNull(options);
            _options = options;
            _tcpClient = tcpClient;
            _stream = stream;
            _stream.ReadTimeout = options.OperationTimeoutMilliseconds;
            _stream.WriteTimeout = options.OperationTimeoutMilliseconds;
        }

        /// <summary>
        /// Opens a connected session to spamd.
        /// </summary>
        /// <param name="options">Connection settings.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A connected wire session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        /// <exception cref="SpamdProtocolException">Thrown when the TCP connection fails.</exception>
        public static async Task<SpamdWireSession> ConnectAsync(SpamAssassinOptions options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(options);
            TcpClient tcpClient = new(options.Host.Contains(':', StringComparison.Ordinal) ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork)
            {
                NoDelay = true,
            };

            try
            {
                using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(options.ConnectTimeoutMilliseconds);
                await tcpClient.ConnectAsync(options.Host, options.Port, connectCts.Token).ConfigureAwait(false);
                NetworkStream stream = tcpClient.GetStream();
                return new SpamdWireSession(options, tcpClient, stream);
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
            {
                tcpClient.Dispose();
                throw new SpamdProtocolException($"Failed to connect to spamd at {options.Host}:{options.Port}.", ex);
            }
        }

        /// <summary>
        /// Disposes the spamd wire session.
        /// </summary>
        /// <returns>A task that completes when the spamd wire session is disposed.</returns>
        public ValueTask DisposeAsync()
        {
            _stream.Dispose();
            _tcpClient.Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Sends a spamd command and optional message body, then reads the full response.
        /// </summary>
        /// <param name="command">spamd command.</param>
        /// <param name="article">Raw message bytes (ignored for <see cref="SpamdCommand.Ping"/> and <see cref="SpamdCommand.Skip"/>).</param>
        /// <param name="extraRequestHeaders">Optional additional request headers (for example <c>TELL</c> metadata).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Parsed status line, header map, and optional body bytes.</returns>
        public async Task<SpamdWireResponse> ExecuteAsync(
            SpamdCommand command,
            ReadOnlyMemory<byte> article,
            IReadOnlyDictionary<string, string>? extraRequestHeaders,
            CancellationToken cancellationToken)
        {
            bool sendBody = command is not SpamdCommand.Ping and not SpamdCommand.Skip;
            await WriteRequestAsync(command, article, sendBody, extraRequestHeaders, cancellationToken).ConfigureAwait(false);
            return await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the spamc request line, headers, optional body, and half-closes the send side.
        /// </summary>
        private async Task WriteRequestAsync(
            SpamdCommand command,
            ReadOnlyMemory<byte> article,
            bool sendBody,
            IReadOnlyDictionary<string, string>? extraRequestHeaders,
            CancellationToken cancellationToken)
        {
            byte[] requestPrefix = BuildRequestHeaderBytes(command, sendBody ? article.Length : 0, extraRequestHeaders);
            await _stream.WriteAsync(requestPrefix.AsMemory(0, requestPrefix.Length), cancellationToken).ConfigureAwait(false);

            if (sendBody && !article.IsEmpty)
            {
                await _stream.WriteAsync(article, cancellationToken).ConfigureAwait(false);
            }

            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            _tcpClient.Client.Shutdown(SocketShutdown.Send);
        }

        /// <summary>
        /// Builds request header bytes ending with the blank line before the message body.
        /// </summary>
        private byte[] BuildRequestHeaderBytes(
            SpamdCommand command,
            int contentLength,
            IReadOnlyDictionary<string, string>? extraRequestHeaders)
        {
            StringBuilder sb = new(128);
            _ = sb.Append(GetCommandName(command));
            _ = sb.Append(" SPAMC/");
            _ = sb.Append(_options.SpamdProtocolVersion);
            _ = sb.Append("\r\n");

            if (contentLength > 0)
            {
                _ = sb.Append("Content-length: ");
                _ = sb.Append(contentLength);
                _ = sb.Append("\r\n");
            }

            if (!string.IsNullOrEmpty(_options.User))
            {
                _ = sb.Append("User: ");
                _ = sb.Append(_options.User);
                _ = sb.Append("\r\n");
            }

            if (extraRequestHeaders is not null)
            {
                foreach (KeyValuePair<string, string> header in extraRequestHeaders)
                {
                    _ = sb.Append(header.Key);
                    _ = sb.Append(": ");
                    _ = sb.Append(header.Value);
                    _ = sb.Append("\r\n");
                }
            }

            _ = sb.Append("\r\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        /// <summary>
        /// Reads the spamd status line, header block, and optional Content-length or trailing body bytes.
        /// </summary>
        private async Task<SpamdWireResponse> ReadResponseAsync(CancellationToken cancellationToken)
        {
            string? statusLine = await ReadAsciiLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(statusLine))
            {
                throw new SpamdProtocolException("spamd returned an empty response.");
            }

            ParseStatusLine(statusLine, out int exitCode, out string statusMessage);
            if (exitCode != 0)
            {
                throw new SpamdProtocolException(exitCode, statusMessage, statusLine);
            }

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                string? headerLine = await ReadAsciiLineAsync(cancellationToken).ConfigureAwait(false);
                if (headerLine is null || headerLine.Length == 0)
                {
                    break;
                }

                int colon = headerLine.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                headers[headerLine[..colon].Trim().ToLowerInvariant()] = headerLine[(colon + 1)..].Trim();
            }

            int contentLength = 0;
            if (headers.TryGetValue("content-length", out string? lenText)
                && int.TryParse(lenText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && parsed >= 0)
            {
                contentLength = parsed;
            }

            byte[] body;
            if (contentLength > 0)
            {
                if (contentLength > MaxResponseBodyBytes)
                {
                    throw new SpamdProtocolException($"spamd Content-length {contentLength} exceeds limit {MaxResponseBodyBytes}.");
                }

                body = new byte[contentLength];
                await ReadExactlyAsync(body.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                body = await ReadAvailableBodyAsync(cancellationToken).ConfigureAwait(false);
            }

            return new SpamdWireResponse(statusLine, headers, body);
        }

        /// <summary>
        /// Reads one CRLF-terminated ASCII line from the spamd stream (without the line terminator).
        /// </summary>
        private async Task<string?> ReadAsciiLineAsync(CancellationToken cancellationToken)
        {
            using MemoryStream lineBuffer = new(256);
            while (true)
            {
                byte[] one = new byte[1];
                int read = await _stream.ReadAsync(one.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return lineBuffer.Length == 0 ? null : Encoding.ASCII.GetString(lineBuffer.GetBuffer(), 0, (int)lineBuffer.Length);
                }

                byte b = one[0];
                if (b == (byte)'\n')
                {
                    return Encoding.ASCII.GetString(lineBuffer.GetBuffer(), 0, (int)lineBuffer.Length);
                }

                if (b != (byte)'\r')
                {
                    lineBuffer.WriteByte(b);
                }

                if (lineBuffer.Length > 64 * 1024)
                {
                    throw new SpamdProtocolException("spamd response line exceeds maximum allowed length.");
                }
            }
        }

        /// <summary>
        /// Reads any bytes still available when no <c>Content-length</c> was supplied (SYMBOLS / REPORT trailers).
        /// </summary>
        private async Task<byte[]> ReadAvailableBodyAsync(CancellationToken cancellationToken)
        {
            using MemoryStream body = new();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(InitialReadBufferSize);
            try
            {
                while (true)
                {
                    int read = await _stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    body.Write(buffer, 0, read);
                    if (body.Length > MaxResponseBodyBytes)
                    {
                        throw new SpamdProtocolException("spamd response body exceeds maximum allowed size.");
                    }
                }

                return body.ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Reads exactly <paramref name="destination"/> bytes from the stream.
        /// </summary>
        private async Task ReadExactlyAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < destination.Length)
            {
                int read = await _stream.ReadAsync(destination[total..], cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new SpamdProtocolException($"spamd closed the connection after {total} of {destination.Length} body bytes.");
                }

                total += read;
            }
        }

        /// <summary>
        /// Maps <paramref name="command"/> to the wire command token.
        /// </summary>
        private static string GetCommandName(SpamdCommand command)
        {
            return command switch
            {
                SpamdCommand.Check => "CHECK",
                SpamdCommand.Symbols => "SYMBOLS",
                SpamdCommand.Report => "REPORT",
                SpamdCommand.ReportIfSpam => "REPORT_IFSPAM",
                SpamdCommand.Process => "PROCESS",
                SpamdCommand.Ping => "PING",
                SpamdCommand.Skip => "SKIP",
                SpamdCommand.Tell => "TELL",
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown spamd command."),
            };
        }

        /// <summary>
        /// Parses <c>SPAMD/x.y CODE MESSAGE</c> from the first line.
        /// </summary>
        private static void ParseStatusLine(string statusLine, out int exitCode, out string statusMessage)
        {
            string[] parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts[0].StartsWith("SPAMD/", StringComparison.OrdinalIgnoreCase))
            {
                throw new SpamdProtocolException($"Unexpected spamd status line: {statusLine}");
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out exitCode))
            {
                throw new SpamdProtocolException($"Unexpected spamd status line: {statusLine}");
            }

            statusMessage = string.Join(' ', parts.Skip(2));
        }

        /// <summary>
        /// Parses the <c>Spam:</c> header into score metadata.
        /// </summary>
        internal static SpamdCheckResult? TryParseSpamHeader(IReadOnlyDictionary<string, string> headers, byte[] trailingBody, SpamdCommand command)
        {
            if (!headers.TryGetValue("spam", out string? spamValue))
            {
                return null;
            }

            Match match = SpamHeaderRegex.Match(spamValue);
            if (!match.Success)
            {
                throw new SpamdProtocolException($"Unrecognized Spam response header: {spamValue}");
            }

            bool isSpam = string.Equals(match.Groups[1].Value, "True", StringComparison.OrdinalIgnoreCase);
            double score = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            double threshold = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

            IReadOnlyList<string> symbols = [];
            string? reportText = null;

            if (command == SpamdCommand.Symbols && trailingBody.Length > 0)
            {
                string symbolLine = Encoding.UTF8.GetString(trailingBody).Trim();
                symbols = string.IsNullOrEmpty(symbolLine)
                    ? []
                    : symbolLine.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            }
            else if (command is SpamdCommand.Report or SpamdCommand.ReportIfSpam && trailingBody.Length > 0)
            {
                reportText = Encoding.UTF8.GetString(trailingBody).TrimEnd();
            }

            return new SpamdCheckResult(isSpam, score, threshold, symbols, reportText, headers);
        }
    }

    /// <summary>
    /// Raw spamd response after wire parsing.
    /// </summary>
    /// <param name="StatusLine">First line (<c>SPAMD/x.y 0 EX_OK</c>).</param>
    /// <param name="Headers">Response headers (keys lower-case).</param>
    /// <param name="Body">Bytes after the header block (symbols, report, or processed message).</param>
    internal readonly record struct SpamdWireResponse(string StatusLine, IReadOnlyDictionary<string, string> Headers, byte[] Body);
}

