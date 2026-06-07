// <copyright file="NntpSocketTransport.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: socket stream bridged to pipelines; supports STARTTLS upgrade.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Vector.NNTP.Sockets.Compression;
using Vector.NNTP.Sockets.Tls;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Owns the accepted <see cref="Socket"/>, underlying stream, and pipe bridge for one NNTP session.
    /// </summary>
    public sealed class NntpSocketTransport : INntpSessionTransport
    {
        private const int DefaultPipeReadBufferBytes = 65_536;
        private const int DefaultMinimumReadSize = 4096;

        private Stream _stream;
        private PipeReader? _input;
        private PipeWriter? _output;
        private readonly int _pipeReadBufferBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSocketTransport"/> class over a cleartext connection.
        /// </summary>
        /// <param name="socket">Accepted TCP socket.</param>
        /// <param name="pipeReadBufferBytes">Stream pipe reader buffer size (defaults to 64 KiB).</param>
        public NntpSocketTransport(Socket socket, int pipeReadBufferBytes = DefaultPipeReadBufferBytes)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            this._pipeReadBufferBytes = pipeReadBufferBytes;
            this._stream = CreateOutboundRateLimitStream(new NetworkStream(socket, ownsSocket: false));
            this.RebindPipes(this._stream);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSocketTransport"/> class over an already established stream.
        /// </summary>
        /// <param name="socket">Accepted TCP socket.</param>
        /// <param name="preboundStream">
        /// Stream already bound to the desired transport mode (for example cleartext with a consumed preamble, or an
        /// authenticated <see cref="SslStream"/>).
        /// </param>
        /// <param name="pipeReadBufferBytes">Stream pipe reader buffer size (defaults to 64 KiB).</param>
        public NntpSocketTransport(Socket socket, Stream preboundStream, int pipeReadBufferBytes = DefaultPipeReadBufferBytes)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            this._pipeReadBufferBytes = pipeReadBufferBytes;
            this._stream = CreateOutboundRateLimitStream(preboundStream ?? throw new ArgumentNullException(nameof(preboundStream)));
            this.RebindPipes(this._stream);
        }

        /// <summary>
        /// Gets the accepted socket (used for STARTTLS upgrade before encryption).
        /// </summary>
        public Socket Socket { get; }

        /// <summary>
        /// Gets the input pipe reader for the session command loop.
        /// </summary>
        public PipeReader Input => _input!;

        /// <summary>
        /// Gets the output pipe writer for the session command loop.
        /// </summary>
        public PipeWriter Output => _output!;

        /// <summary>
        /// Upgrades a cleartext session to TLS after STARTTLS (RFC 4642).
        /// </summary>
        /// <param name="serverCertificate">Server certificate with private key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when pipes are rebound to the negotiated TLS stream.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="serverCertificate"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the transport is already TLS-protected.</exception>
        public async Task UpgradeToTlsAsync(X509Certificate2 serverCertificate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(serverCertificate);
            if (_stream is SslStream)
            {
                throw new InvalidOperationException("Transport is already TLS-protected.");
            }

            await CompletePipesAsync().ConfigureAwait(false);

            SslStream ssl = new(_stream, leaveInnerStreamOpen: false);
            await NntpTlsHandshake.AuthenticateServerAsync(ssl, serverCertificate, cancellationToken).ConfigureAwait(false);
            _stream = ssl;
            RebindPipes(_stream);
        }

        /// <summary>
        /// Activates RFC 8054 COMPRESS DEFLATE on the transport after the 206 response is sent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when compression is active.</returns>
        /// <exception cref="InvalidOperationException">Thrown when compression is already active.</exception>
        public async ValueTask ActivateDeflateCompressionAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (_stream is NntpZLibSessionStream)
            {
                throw new InvalidOperationException("Transport compression is already active.");
            }

            await CompletePipesAsync().ConfigureAwait(false);
            _stream = new NntpZLibSessionStream(_stream);
            RebindPipes(_stream);
        }

        /// <summary>
        /// Wraps the outbound stream with a dynamic send rate limiter and rebuilds the pipe bridge.
        /// </summary>
        /// <param name="bytesPerSecond">Initial per-session send cap.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The rate limiter instance when applied.</returns>
        public ValueTask<DynamicSendRateLimitedStream?> ApplyOutboundRateLimitAsync(long bytesPerSecond, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (_stream is DynamicSendRateLimitedStream existing)
            {
                existing.UpdateMaxSendBytesPerSecond(bytesPerSecond);
                return new ValueTask<DynamicSendRateLimitedStream?>(existing);
            }

            throw new InvalidOperationException("Outbound rate limiter was not installed at transport creation.");
        }

        private static DynamicSendRateLimitedStream CreateOutboundRateLimitStream(Stream inner)
        {
            return new DynamicSendRateLimitedStream(inner, initialMaxSendBytesPerSecond: 0, leaveInnerOpen: false);
        }

        /// <summary>
        /// Rebinds the transport pipes to the current underlying stream.
        /// </summary>
        /// <param name="stream">Underlying stream.</param>
        private void RebindPipes(Stream stream)
        {
            StreamPipeReaderOptions readerOptions = new(
                bufferSize: this._pipeReadBufferBytes,
                minimumReadSize: DefaultMinimumReadSize,
                leaveOpen: true);
            StreamPipeWriterOptions writerOptions = new(leaveOpen: true);
            _input = PipeReader.Create(stream, readerOptions);
            _output = PipeWriter.Create(stream, writerOptions);
        }

        /// <summary>
        /// Completes the current pipe reader and writer, if any.
        /// </summary>
        /// <returns>A task that completes when the pipes are completed.</returns>
        private async Task CompletePipesAsync()
        {
            if (_input is not null)
            {
                _input.CancelPendingRead();
            }

            if (_output is not null)
            {
                _output.CancelPendingFlush();
            }

            if (_output is not null)
            {
                try
                {
                    await _output.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort completion; the underlying stream may already be faulted/closed.
                }

                _output = null;
            }

            if (_input is not null)
            {
                try
                {
                    await _input.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort completion; the underlying stream may already be faulted/closed.
                }

                _input = null;
            }
        }

        /// <summary>
        /// Disposes the transport.
        /// </summary>
        /// <returns>A task that completes when the transport is disposed.</returns>
        /// <exception cref="Exception">Thrown when an error occurs while disposing the transport.</exception>
        public async ValueTask DisposeAsync()
        {
            if (Socket.Connected)
            {
                try
                {
                    Socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException)
                {
                    // Peer may already have closed the connection.
                }
            }

            await CompletePipesAsync().ConfigureAwait(false);

            await _stream.DisposeAsync().ConfigureAwait(false);
            Socket.Dispose();
        }
    }
}
