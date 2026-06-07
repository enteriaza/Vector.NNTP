// <copyright file="SpamdWireSession.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: per-article spamd response read for POST-filter scanning; one TCP session per request; buffered stream I/O.
// SpamdWireSession.cs -- Low-level spamc/spamd framing, header IO, and response parsing.

using System.Buffers;
using System.Globalization;

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// Executes a single spamd request/response exchange over TCP.
    /// </summary>
    /// <remarks>
    /// <para><b>Performance:</b> HOT PATH on the POST filter — articles under the configured size threshold are scanned via
    /// spamd on every accepted post. Response headers are read through a pooled 8 KiB buffer rather than one-byte stream reads.</para>
    /// <para><b>Protocol:</b> Implements spamc request framing and spamd response parsing per
    /// <see href="https://apache.googlesource.com/spamassassin/+/de1db4d804b4bde5d91101f4870dc3cdbf4af688/3.1/spamd/PROTOCOL">spamd/PROTOCOL</see>.</para>
    /// <para><b>Lifecycle:</b> One session per request; send side is half-closed after the article is written per spamc protocol.</para>
    /// </remarks>
    internal sealed class SpamdWireSession : IAsyncDisposable
    {
        /// <summary>
        /// Maximum spamd response body size (64 MiB) to bound memory use when reading <c>Content-length</c> bodies or trailers.
        /// </summary>
        private const int MaxResponseBodyBytes = 64 * 1024 * 1024;

        /// <summary>
        /// Initial capacity of the pooled response read buffer and the chunk size used for trailing-body reads.
        /// </summary>
        private const int InitialReadBufferSize = 8192;

        /// <summary>
        /// Maximum allowed length of a single spamd response line (64 KiB), including the status line and header lines.
        /// </summary>
        private const int MaxResponseLineBytes = 64 * 1024;

        /// <summary>
        /// Pooled read buffer rented on first response read and returned from <see cref="DisposeAsync"/>.
        /// </summary>
        /// <remarks>
        /// Lazily allocated by <see cref="EnsureResponseReadBuffer"/>; holds unconsumed bytes between
        /// <see cref="RefillResponseReadBufferAsync"/> calls.
        /// </remarks>
        private byte[]? _responseReadBuffer;

        /// <summary>
        /// Index of the next unconsumed byte in <see cref="_responseReadBuffer"/>.
        /// </summary>
        private int _responseReadOffset;

        /// <summary>
        /// Number of valid bytes currently stored in <see cref="_responseReadBuffer"/> (exclusive upper bound for reads).
        /// </summary>
        private int _responseReadCount;

        /// <summary>
        /// Connected TCP client to the selected spamd host; disposed with the session.
        /// </summary>
        private readonly TcpClient _tcpClient;

        /// <summary>
        /// Network stream over <see cref="_tcpClient"/> used for request writes and response reads until disposed.
        /// </summary>
        private readonly NetworkStream _stream;

        /// <summary>
        /// Options snapshot captured at connect time (protocol version, user header, stream timeouts).
        /// </summary>
        private readonly SpamAssassinOptions _options;

        /// <summary>
        /// Initializes a connected session after <see cref="ConnectAsync"/> completes TCP setup.
        /// </summary>
        /// <param name="options">Host, port, protocol version, user header, and timeout settings.</param>
        /// <param name="tcpClient">Connected client; ownership transfers to this instance.</param>
        /// <param name="stream">Stream returned from <see cref="TcpClient.GetStream"/> for <paramref name="tcpClient"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Applies <see cref="SpamAssassinOptions.OperationTimeoutMilliseconds"/> to <see cref="NetworkStream.ReadTimeout"/> and
        /// <see cref="NetworkStream.WriteTimeout"/> on <paramref name="stream"/>.
        /// </remarks>
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
        /// <param name="host">spamd hostname or IP address.</param>
        /// <param name="options">Connection settings (port and timeouts).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A connected wire session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="host"/> is null or empty.</exception>
        /// <exception cref="SpamdConnectionException">
        /// Thrown when the TCP connection fails. <see cref="SocketException"/>, <see cref="IOException"/>, and
        /// <see cref="OperationCanceledException"/> (connect timeout or caller cancellation) are wrapped as the inner exception.
        /// </exception>
        /// <remarks>
        /// <para><b>Contract:</b> Throws only <see cref="SpamdConnectionException"/> — never <see cref="SpamdProtocolException"/> — so callers can
        /// distinguish connect failures from post-connect protocol errors.</para>
        /// <para>Uses a dual-stack <see cref="TcpClient"/> with <see cref="Socket.NoDelay"/> enabled and lets
        /// <see cref="TcpClient.ConnectAsync(string, int, CancellationToken)"/> resolve the address family from DNS
        /// (avoids inferring IPv6 from a literal colon in hostnames).</para>
        /// <para>Connect timeout is bounded by <see cref="SpamAssassinOptions.ConnectTimeoutMilliseconds"/> linked to <paramref name="cancellationToken"/>.</para>
        /// </remarks>
        public static async Task<SpamdWireSession> ConnectAsync(
            string host,
            SpamAssassinOptions options,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(host);
            ArgumentNullException.ThrowIfNull(options);
            TcpClient tcpClient = new()
            {
                NoDelay = true,
            };

            try
            {
                using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(options.ConnectTimeoutMilliseconds);
                await tcpClient.ConnectAsync(host, options.Port, connectCts.Token).ConfigureAwait(false);
                NetworkStream stream = tcpClient.GetStream();
                return new SpamdWireSession(options, tcpClient, stream);
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
            {
                tcpClient.Dispose();
                throw new SpamdConnectionException($"Failed to connect to spamd at {host}:{options.Port}.", ex);
            }
        }

        /// <summary>
        /// Releases the pooled response buffer, network stream, and TCP client for this one-shot session.
        /// </summary>
        /// <returns>A completed <see cref="ValueTask"/>; no asynchronous I/O is performed.</returns>
        /// <remarks>
        /// <para>Does not await in-flight <see cref="ExecuteAsync"/> work — callers must finish or abandon the command before disposal.</para>
        /// <para>Returns a rented <see cref="_responseReadBuffer"/> to <see cref="ArrayPool{T}.Shared"/> when present, then disposes
        /// <see cref="_stream"/> and <see cref="_tcpClient"/>.</para>
        /// </remarks>
        public ValueTask DisposeAsync()
        {
            if (_responseReadBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_responseReadBuffer);
                _responseReadBuffer = null;
            }

            _stream.Dispose();
            _tcpClient.Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Sends a spamd command and optional message body, then reads and parses the full response.
        /// </summary>
        /// <param name="command">Wire command (for example <see cref="SpamdCommand.Check"/> or <see cref="SpamdCommand.Ping"/>).</param>
        /// <param name="article">Raw message bytes (ignored for <see cref="SpamdCommand.Ping"/> and <see cref="SpamdCommand.Skip"/>).</param>
        /// <param name="extraRequestHeaders">Optional additional request headers (for example <c>TELL</c> metadata); may be <see langword="null"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Parsed status line, response header map, and optional body bytes.</returns>
        /// <exception cref="SpamdProtocolException">
        /// Thrown when the request cannot be sent, the response is malformed, spamd returns a non-zero exit code, or body limits are exceeded.
        /// </exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled during I/O.</exception>
        /// <remarks>
        /// <para><b>Protocol:</b> Writes the spamc header block, optional article body, flushes, then half-closes the send side before reading the response.</para>
        /// <para><b>Lifecycle:</b> Intended as a single exchange per session instance; callers dispose the session after this method returns.</para>
        /// </remarks>
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
        /// <param name="command">Wire command token source.</param>
        /// <param name="article">Article octets written after the header block when <paramref name="sendBody"/> is <see langword="true"/>.</param>
        /// <param name="sendBody">When <see langword="false"/>, omits the message body and <c>Content-length</c> header.</param>
        /// <param name="extraRequestHeaders">Optional additional request headers; may be <see langword="null"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the request prefix, optional body, and flush have finished.</returns>
        /// <exception cref="IOException">Thrown when the underlying stream write or flush fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <remarks>
        /// Send-side <see cref="Socket.Shutdown"/> ignores <see cref="SocketException"/> and
        /// <see cref="ObjectDisposedException"/> when the peer or host has already torn down the socket.
        /// </remarks>
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
            try
            {
                _tcpClient.Client.Shutdown(SocketShutdown.Send);
            }
            catch (SocketException)
            {
                // Peer may already have closed the connection before half-close.
            }
            catch (ObjectDisposedException)
            {
                // TcpClient/socket may be disposed if spamd or the host races teardown.
            }
        }

        /// <summary>
        /// Builds ASCII request header bytes ending with the blank line that precedes the optional message body.
        /// </summary>
        /// <param name="command">Wire command used for the first request line.</param>
        /// <param name="contentLength">Article length for the <c>Content-length</c> header; omit when zero.</param>
        /// <param name="extraRequestHeaders">Optional additional headers appended before the terminating blank line.</param>
        /// <returns>New byte array containing <c>COMMAND SPAMC/x.y</c>, optional <c>Content-length</c>, optional <c>User:</c>, extras, and <c>\r\n\r\n</c>.</returns>
        /// <remarks>
        /// Encodes with <see cref="Encoding.ASCII"/>; request lines and header names must therefore be ASCII-safe.
        /// </remarks>
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
        /// Reads the spamd status line, header block, and response body per <c>Content-length</c> or trailing bytes.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Structured wire response with status line, header dictionary, and body bytes (possibly empty).</returns>
        /// <exception cref="SpamdProtocolException">
        /// Thrown when the status line is missing, malformed, or reports a non-zero exit code; when a header line or body exceeds configured limits;
        /// or when the stream ends before the declared body length is satisfied.
        /// </exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <remarks>
        /// Header names are stored with wire casing; lookup uses <see cref="StringComparer.OrdinalIgnoreCase"/>.
        /// When <c>Content-length</c> is absent or zero, remaining stream bytes are read as the body (typical for <c>SYMBOLS</c> / <c>REPORT</c> trailers).
        /// </remarks>
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

                headers[headerLine[..colon].Trim()] = headerLine[(colon + 1)..].Trim();
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
        /// Ensures <see cref="_responseReadBuffer"/> is rented from <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <remarks>
        /// Called before the first response read; the buffer is retained for the lifetime of the session until <see cref="DisposeAsync"/>.
        /// </remarks>
        private void EnsureResponseReadBuffer()
        {
            _responseReadBuffer ??= ArrayPool<byte>.Shared.Rent(InitialReadBufferSize);
        }

        /// <summary>
        /// Refills <see cref="_responseReadBuffer"/> from the network stream, discarding any previously consumed bytes.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Bytes read into the buffer, or <c>0</c> when the stream has ended.</returns>
        /// <exception cref="IOException">Thrown when the underlying stream read fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
        private async Task<int> RefillResponseReadBufferAsync(CancellationToken cancellationToken)
        {
            EnsureResponseReadBuffer();
            _responseReadOffset = 0;
            int read = await _stream.ReadAsync(_responseReadBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            _responseReadCount = read;
            return read;
        }

        /// <summary>
        /// Reads one CRLF-terminated ASCII line from the spamd stream (without the line terminator).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The line text, or <see langword="null"/> when the stream ends before any bytes are read.</returns>
        /// <remarks>
        /// Consumes lines from <see cref="_responseReadBuffer"/> in up-to-<see cref="InitialReadBufferSize"/> byte chunks so
        /// typical header blocks require only a handful of TCP reads instead of one syscall per octet.
        /// </remarks>
        /// <exception cref="SpamdProtocolException">Thrown when a line exceeds <see cref="MaxResponseLineBytes"/>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
        private async Task<string?> ReadAsciiLineAsync(CancellationToken cancellationToken)
        {
            EnsureResponseReadBuffer();
            byte[] readBuffer = _responseReadBuffer!;
            MemoryStream? lineAccumulator = null;

            try
            {
                while (true)
                {
                    if (_responseReadOffset >= _responseReadCount)
                    {
                        int read = await RefillResponseReadBufferAsync(cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            return lineAccumulator is null || lineAccumulator.Length == 0
                                ? null
                                : Encoding.ASCII.GetString(lineAccumulator.GetBuffer(), 0, (int)lineAccumulator.Length);
                        }
                    }

                    if (TryConsumeBufferedAsciiLine(readBuffer, ref _responseReadOffset, _responseReadCount, ref lineAccumulator, out string? line))
                    {
                        return line;
                    }
                }
            }
            finally
            {
                lineAccumulator?.Dispose();
            }
        }

        /// <summary>
        /// Attempts to consume one CRLF-terminated line from buffered response bytes.
        /// </summary>
        /// <param name="buffer">Active response read buffer.</param>
        /// <param name="offset">Next unconsumed byte index; updated when bytes are consumed.</param>
        /// <param name="count">Number of valid bytes in <paramref name="buffer"/>.</param>
        /// <param name="lineAccumulator">Optional accumulator for lines spanning multiple buffer fills.</param>
        /// <param name="line">When this method returns <see langword="true"/>, the decoded line without CRLF.</param>
        /// <returns><see langword="true"/> when a complete line was parsed; otherwise more buffered or streamed data is required.</returns>
        /// <remarks>
        /// When a multi-chunk line completes, <paramref name="lineAccumulator"/> is reset so a future refactor can reuse it across lines.
        /// </remarks>
        /// <exception cref="SpamdProtocolException">Thrown when a line exceeds <see cref="MaxResponseLineBytes"/>.</exception>
        private static bool TryConsumeBufferedAsciiLine(
            byte[] buffer,
            ref int offset,
            int count,
            ref MemoryStream? lineAccumulator,
            out string? line)
        {
            line = null;
            ReadOnlySpan<byte> available = buffer.AsSpan(offset, count - offset);
            int newlineIndex = available.IndexOf((byte)'\n');
            if (newlineIndex >= 0)
            {
                ReadOnlySpan<byte> linePart = available[..newlineIndex];
                if (!linePart.IsEmpty && linePart[^1] == (byte)'\r')
                {
                    linePart = linePart[..^1];
                }

                offset += newlineIndex + 1;

                if (lineAccumulator is null)
                {
                    if (linePart.Length > MaxResponseLineBytes)
                    {
                        throw new SpamdProtocolException("spamd response line exceeds maximum allowed length.");
                    }

                    line = linePart.IsEmpty ? string.Empty : Encoding.ASCII.GetString(linePart);
                    return true;
                }

                lineAccumulator.Write(linePart);
                if (lineAccumulator.Length > MaxResponseLineBytes)
                {
                    throw new SpamdProtocolException("spamd response line exceeds maximum allowed length.");
                }

                line = Encoding.ASCII.GetString(lineAccumulator.GetBuffer(), 0, (int)lineAccumulator.Length);
                lineAccumulator.SetLength(0);
                return true;
            }

            if (!available.IsEmpty)
            {
                lineAccumulator ??= new MemoryStream(256);
                lineAccumulator.Write(available);
                offset = count;

                if (lineAccumulator.Length > MaxResponseLineBytes)
                {
                    throw new SpamdProtocolException("spamd response line exceeds maximum allowed length.");
                }
            }

            return false;
        }

        /// <summary>
        /// Reads any bytes still available when no positive <c>Content-length</c> was supplied (for example <c>SYMBOLS</c> or <c>REPORT</c> trailers).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>All remaining response bytes, which may be an empty array when spamd sends no trailer.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when accumulated body size exceeds <see cref="MaxResponseBodyBytes"/>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <remarks>
        /// Drains any bytes already present in <see cref="_responseReadBuffer"/> after header parsing before reading additional chunks from the stream.
        /// </remarks>
        private async Task<byte[]> ReadAvailableBodyAsync(CancellationToken cancellationToken)
        {
            EnsureResponseReadBuffer();
            byte[] readBuffer = _responseReadBuffer!;
            using MemoryStream body = new();

            if (_responseReadOffset < _responseReadCount)
            {
                body.Write(readBuffer, _responseReadOffset, _responseReadCount - _responseReadOffset);
                _responseReadOffset = _responseReadCount;
            }

            while (true)
            {
                int read = await RefillResponseReadBufferAsync(cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                body.Write(readBuffer, 0, read);
                if (body.Length > MaxResponseBodyBytes)
                {
                    throw new SpamdProtocolException("spamd response body exceeds maximum allowed size.");
                }
            }

            return body.ToArray();
        }

        /// <summary>
        /// Fills <paramref name="destination"/> with exactly its length in bytes from the buffered response stream.
        /// </summary>
        /// <param name="destination">Receive buffer; length must match spamd <c>Content-length</c>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when <paramref name="destination"/> is fully populated.</returns>
        /// <exception cref="SpamdProtocolException">Thrown when the stream ends before <paramref name="destination"/> is filled.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
        /// <remarks>
        /// Consumes any bytes already buffered after header parsing before issuing additional stream reads.
        /// </remarks>
        private async Task ReadExactlyAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            EnsureResponseReadBuffer();
            byte[] readBuffer = _responseReadBuffer!;
            int total = 0;
            while (total < destination.Length)
            {
                if (_responseReadOffset < _responseReadCount)
                {
                    int available = _responseReadCount - _responseReadOffset;
                    int toCopy = Math.Min(available, destination.Length - total);
                    readBuffer.AsMemory(_responseReadOffset, toCopy).CopyTo(destination.Slice(total, toCopy));
                    _responseReadOffset += toCopy;
                    total += toCopy;
                    continue;
                }

                int read = await RefillResponseReadBufferAsync(cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new SpamdProtocolException($"spamd closed the connection after {total} of {destination.Length} body bytes.");
                }
            }
        }

        /// <summary>
        /// Maps <paramref name="command"/> to the spamc wire command token (for example <c>CHECK</c>).
        /// </summary>
        /// <param name="command">Logical spamd command.</param>
        /// <returns>Upper-case command name sent on the first request line.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="command"/> is not a defined <see cref="SpamdCommand"/> value.</exception>
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
        /// Parses <c>SPAMD/x.y CODE MESSAGE</c> from the first line without allocating split arrays.
        /// </summary>
        /// <param name="statusLine">Raw status line from spamd.</param>
        /// <param name="exitCode">Parsed sysexits code (for example <c>0</c> for <c>EX_OK</c>).</param>
        /// <param name="statusMessage">Text after the numeric code.</param>
        /// <exception cref="SpamdProtocolException">Thrown when the line does not match the expected spamd format.</exception>
        private static void ParseStatusLine(string statusLine, out int exitCode, out string statusMessage)
        {
            ReadOnlySpan<char> span = statusLine.AsSpan().Trim();
            int firstSpace = span.IndexOf(' ');
            if (firstSpace <= 0 || !span[..firstSpace].StartsWith("SPAMD/", StringComparison.OrdinalIgnoreCase))
            {
                throw new SpamdProtocolException($"Unexpected spamd status line: {statusLine}");
            }

            span = span[(firstSpace + 1)..].TrimStart();
            int codeEnd = span.IndexOf(' ');
            if (codeEnd <= 0)
            {
                throw new SpamdProtocolException($"Unexpected spamd status line: {statusLine}");
            }

            if (!int.TryParse(span[..codeEnd], NumberStyles.Integer, CultureInfo.InvariantCulture, out exitCode))
            {
                throw new SpamdProtocolException($"Unexpected spamd status line: {statusLine}");
            }

            statusMessage = span[(codeEnd + 1)..].Trim().ToString();
        }

        /// <summary>
        /// Parses a spamd <c>Spam:</c> header value of the form <c>True ; score / threshold</c> or <c>False ; score / threshold</c>.
        /// </summary>
        /// <param name="value">Header value text after the <c>Spam:</c> name and colon.</param>
        /// <param name="isSpam">When this method returns <see langword="true"/>, whether spamd marked the message as spam.</param>
        /// <param name="score">When this method returns <see langword="true"/>, the message score from the header.</param>
        /// <param name="threshold">When this method returns <see langword="true"/>, the required threshold from the header.</param>
        /// <returns><see langword="true"/> when <paramref name="value"/> matches the expected spamd format; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// Parsing is culture-invariant; numeric portions use <see cref="CultureInfo.InvariantCulture"/>.
        /// </remarks>
        private static bool TryParseSpamHeaderValue(ReadOnlySpan<char> value, out bool isSpam, out double score, out double threshold)
        {
            isSpam = false;
            score = 0;
            threshold = 0;

            value = value.Trim();
            if (value.IsEmpty)
            {
                return false;
            }

            int semicolon = value.IndexOf(';');
            if (semicolon < 0)
            {
                return false;
            }

            ReadOnlySpan<char> flagPart = value[..semicolon].Trim();
            if (flagPart.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                isSpam = true;
            }
            else if (flagPart.Equals("False", StringComparison.OrdinalIgnoreCase))
            {
                isSpam = false;
            }
            else
            {
                return false;
            }

            ReadOnlySpan<char> numbers = value[(semicolon + 1)..].Trim();
            int slash = numbers.IndexOf('/');
            if (slash < 0)
            {
                return false;
            }

            ReadOnlySpan<char> scoreSpan = numbers[..slash].Trim();
            ReadOnlySpan<char> thresholdSpan = numbers[(slash + 1)..].Trim();
            return double.TryParse(scoreSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out score)
                && double.TryParse(thresholdSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out threshold);
        }

        /// <summary>
        /// Parses the <c>Spam:</c> response header and optional command-specific trailing body into a <see cref="SpamdCheckResult"/>.
        /// </summary>
        /// <param name="headers">Parsed response headers (case-insensitive lookup).</param>
        /// <param name="trailingBody">Bytes after the header block (symbols line, report text, or empty).</param>
        /// <param name="command">Command that produced the response; selects how <paramref name="trailingBody"/> is interpreted.</param>
        /// <returns>
        /// A populated <see cref="SpamdCheckResult"/> when a <c>Spam:</c> header is present; otherwise <see langword="null"/>.
        /// </returns>
        /// <exception cref="SpamdProtocolException">Thrown when a <c>Spam:</c> header is present but its value is not recognized.</exception>
        /// <remarks>
        /// <para><b>Symbols:</b> For <see cref="SpamdCommand.Symbols"/>, <paramref name="trailingBody"/> is split on commas into rule names.</para>
        /// <para><b>Report:</b> For <see cref="SpamdCommand.Report"/> and <see cref="SpamdCommand.ReportIfSpam"/>, <paramref name="trailingBody"/> becomes <see cref="SpamdCheckResult.ReportText"/>.</para>
        /// </remarks>
        internal static SpamdCheckResult? TryParseSpamHeader(IReadOnlyDictionary<string, string> headers, byte[] trailingBody, SpamdCommand command)
        {
            if (!headers.TryGetValue("spam", out string? spamValue))
            {
                return null;
            }

            if (!TryParseSpamHeaderValue(spamValue.AsSpan(), out bool isSpam, out double score, out double threshold))
            {
                throw new SpamdProtocolException($"Unrecognized Spam response header: {spamValue}");
            }

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
    /// Raw spamd response after wire parsing in <see cref="SpamdWireSession.ReadResponseAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Body:</b> Empty when spamd sends no trailer and no positive <c>Content-length</c>; otherwise holds symbols, report text, or a processed message.</para>
    /// <para><b>Headers:</b> Excludes the status line; keys preserve wire casing with case-insensitive lookup.</para>
    /// </remarks>
    /// <param name="StatusLine">First line (<c>SPAMD/x.y 0 EX_OK</c> or error variant).</param>
    /// <param name="Headers">Response headers (case-insensitive lookup; keys preserve wire casing).</param>
    /// <param name="Body">Bytes after the header block (symbols, report, processed message, or empty).</param>
    internal readonly record struct SpamdWireResponse(string StatusLine, IReadOnlyDictionary<string, string> Headers, byte[] Body);
}
