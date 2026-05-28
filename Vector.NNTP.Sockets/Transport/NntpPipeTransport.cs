// <copyright file="NntpPipeTransport.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory pipe transport for protocol unit tests.

using System.Security.Cryptography.X509Certificates;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Pipe-backed session transport for golden transcript tests (no TCP socket).
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpPipeTransport"/> class.
    /// </remarks>
    /// <param name="input">Server-side input reader (client writes here).</param>
    /// <param name="output">Server-side output writer (client reads here).</param>
    internal sealed class NntpPipeTransport(PipeReader input, PipeWriter output) : INntpSessionTransport
    {
        private readonly PipeWriter _output = output ?? throw new ArgumentNullException(nameof(output));
        private readonly PipeReader _input = input;

        /// <summary>
        /// Gets the input pipe reader for the command loop.
        /// </summary>
        public PipeReader Input { get; } = input ?? throw new ArgumentNullException(nameof(input));

        /// <summary>
        /// Gets the output pipe writer for responses.
        /// </summary>
        public PipeWriter Output { get; } = output;

        /// <summary>
        /// Upgrades a cleartext session to TLS (STARTTLS).
        /// </summary>
        /// <param name="serverCertificate">Server certificate with private key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when TLS is active.</returns>
        /// <exception cref="NotSupportedException">Thrown when STARTTLS is not supported on in-memory pipe transports.</exception>
        Task INntpSessionTransport.UpgradeToTlsAsync(X509Certificate2 serverCertificate, CancellationToken cancellationToken)
        {
            _ = serverCertificate;
            _ = cancellationToken;
            throw new NotSupportedException("STARTTLS is not supported on in-memory pipe transports.");
        }

        /// <summary>
        /// Activates RFC 8054 COMPRESS DEFLATE on the transport after the 206 response is sent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when compression is active.</returns>
        /// <exception cref="NotSupportedException">Thrown when COMPRESS is not supported on in-memory pipe transports.</exception>
        ValueTask INntpSessionTransport.ActivateDeflateCompressionAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotSupportedException("COMPRESS is not supported on in-memory pipe transports.");
        }

        /// <summary>
        /// Disposes the transport.
        /// </summary>
        /// <returns>A task that completes when the transport is disposed.</returns>
        /// <exception cref="Exception">Thrown when an error occurs while disposing the transport.</exception>
        public async ValueTask DisposeAsync()
        {
            await _output.CompleteAsync().ConfigureAwait(false);
            await _input.CompleteAsync().ConfigureAwait(false);
        }
    }
}
