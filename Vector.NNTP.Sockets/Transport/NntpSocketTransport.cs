// <copyright file="NntpSocketTransport.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: socket stream bridged to pipelines; supports STARTTLS upgrade.

namespace Vector.NNTP.Sockets.Transport
{
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    using Compression;
    using Tls;

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
            this.Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            this._stream = new NetworkStream(socket, ownsSocket: false);
            this._bridge = new NntpStreamPipeBridge(this._stream);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSocketTransport"/> class over an existing encrypted stream.
        /// </summary>
        /// <param name="socket">Accepted TCP socket.</param>
        /// <param name="encryptedStream">TLS-authenticated stream (for example <see cref="SslStream"/>).</param>
        public NntpSocketTransport(Socket socket, Stream encryptedStream)
        {
            this.Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            this._stream = encryptedStream ?? throw new ArgumentNullException(nameof(encryptedStream));
            this._bridge = new NntpStreamPipeBridge(this._stream);
        }

        /// <summary>
        /// Gets the accepted socket (used for STARTTLS upgrade before encryption).
        /// </summary>
        public Socket Socket { get; }

        /// <summary>
        /// Gets the input pipe reader for the session command loop.
        /// </summary>
        public PipeReader Input => this._bridge!.Input;

        /// <summary>
        /// Gets the output pipe writer for the session command loop.
        /// </summary>
        public PipeWriter Output => this._bridge!.Output;

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
            if (this._stream is SslStream)
            {
                throw new InvalidOperationException("Transport is already TLS-protected.");
            }

            await this._bridge!.DisposeAsync().ConfigureAwait(false);
            this._bridge = null;

            var ssl = new SslStream(this._stream, leaveInnerStreamOpen: false);
            await NntpTlsHandshake.AuthenticateServerAsync(ssl, serverCertificate, cancellationToken).ConfigureAwait(false);
            this._stream = ssl;
            this._bridge = new NntpStreamPipeBridge(this._stream);
        }

        /// <inheritdoc />
        public async ValueTask ActivateDeflateCompressionAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (this._stream is NntpZLibSessionStream)
            {
                throw new InvalidOperationException("Transport compression is already active.");
            }

            await this._bridge!.DisposeAsync().ConfigureAwait(false);
            this._bridge = null;
            this._stream = new NntpZLibSessionStream(this._stream);
            this._bridge = new NntpStreamPipeBridge(this._stream);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (this.Socket.Connected)
            {
                try
                {
                    this.Socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException)
                {
                    // Peer may already have closed the connection.
                }
            }

            if (this._bridge is not null)
            {
                await this._bridge.DisposeAsync().ConfigureAwait(false);
                this._bridge = null;
            }

            await this._stream.DisposeAsync().ConfigureAwait(false);
            this.Socket.Dispose();
        }
    }
}
