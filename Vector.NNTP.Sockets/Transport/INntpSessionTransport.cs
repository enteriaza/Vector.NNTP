// <copyright file="INntpSessionTransport.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: transport abstraction for socket and in-memory pipe sessions.

namespace Vector.NNTP.Sockets.Transport
{
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Duplex transport for one NNTP session (TCP socket or test pipes).
    /// </summary>
    public interface INntpSessionTransport : IAsyncDisposable
    {
        /// <summary>
        /// Gets the input pipe reader for the command loop.
        /// </summary>
        PipeReader Input { get; }

        /// <summary>
        /// Gets the output pipe writer for responses.
        /// </summary>
        PipeWriter Output { get; }

        /// <summary>
        /// Upgrades a cleartext session to TLS (STARTTLS).
        /// </summary>
        /// <param name="serverCertificate">Server certificate with private key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when TLS is active.</returns>
        Task UpgradeToTlsAsync(X509Certificate2 serverCertificate, CancellationToken cancellationToken);

        /// <summary>
        /// Activates RFC 8054 COMPRESS DEFLATE on the transport after the 206 response is sent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when compression is active.</returns>
        ValueTask ActivateDeflateCompressionAsync(CancellationToken cancellationToken);
    }
}
