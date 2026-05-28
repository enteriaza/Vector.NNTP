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
        private Stream _stream;
        private NntpStreamPipeBridge? _bridge;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSocketTransport"/> class over a cleartext connection.
        /// </summary>
        /// <param name="socket">Accepted TCP socket.</param>
        public NntpSocketTransport(Socket socket)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _stream = CreateOutboundRateLimitStream(new NetworkStream(socket, ownsSocket: false));
            _bridge = new NntpStreamPipeBridge(_stream);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSocketTransport"/> class over an existing encrypted stream.
        /// </summary>
        /// <param name="socket">Accepted TCP socket.</param>
        /// <param name="encryptedStream">TLS-authenticated stream (for example <see cref="SslStream"/>).</param>
        public NntpSocketTransport(Socket socket, Stream encryptedStream)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _stream = CreateOutboundRateLimitStream(encryptedStream ?? throw new ArgumentNullException(nameof(encryptedStream)));
            _bridge = new NntpStreamPipeBridge(_stream);
        }

        /// <summary>
        /// Gets the accepted socket (used for STARTTLS upgrade before encryption).
        /// </summary>
        public Socket Socket { get; }

        /// <summary>
        /// Gets the input pipe reader for the session command loop.
        /// </summary>
        public PipeReader Input => _bridge!.Input;

        /// <summary>
        /// Gets the output pipe writer for the session command loop.
        /// </summary>
        public PipeWriter Output => _bridge!.Output;

        /// <summary>
        /// Upgrades a cleartext session to TLS after STARTTLS (RFC 4642).
        /// </summary>
        /// <param name="serverCertificate">Server certificate with private key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when the transport is re-bound to TLS.</returns>
        /// <inheritdoc />
        public async Task UpgradeToTlsAsync(X509Certificate2 serverCertificate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(serverCertificate);
            if (_stream is SslStream)
            {
                throw new InvalidOperationException("Transport is already TLS-protected.");
            }

            await _bridge!.DisposeAsync().ConfigureAwait(false);
            _bridge = null;

            SslStream ssl = new(_stream, leaveInnerStreamOpen: false);
            await NntpTlsHandshake.AuthenticateServerAsync(ssl, serverCertificate, cancellationToken).ConfigureAwait(false);
            _stream = ssl;
            _bridge = new NntpStreamPipeBridge(_stream);
        }

        /// <inheritdoc />
        public async ValueTask ActivateDeflateCompressionAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (_stream is NntpZLibSessionStream)
            {
                throw new InvalidOperationException("Transport compression is already active.");
            }

            await _bridge!.DisposeAsync().ConfigureAwait(false);
            _bridge = null;
            _stream = new NntpZLibSessionStream(_stream);
            _bridge = new NntpStreamPipeBridge(_stream);
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

        /// <inheritdoc />
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

            if (_bridge is not null)
            {
                await _bridge.DisposeAsync().ConfigureAwait(false);
                _bridge = null;
            }

            await _stream.DisposeAsync().ConfigureAwait(false);
            Socket.Dispose();
        }
    }
}
