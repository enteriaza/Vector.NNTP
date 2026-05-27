// <copyright file="NntpTlsHandshake.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: server-side TLS handshake for implicit TLS and STARTTLS.

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Vector.NNTP.Sockets.Tls
{
    /// <summary>
    /// Performs TLS server authentication on an <see cref="SslStream"/> using the active server certificate.
    /// </summary>
    internal static class NntpTlsHandshake
    {
        /// <summary>
        /// Authenticates the server side of an <see cref="SslStream"/> with the given certificate.
        /// </summary>
        /// <param name="sslStream">SSL stream over the TCP connection.</param>
        /// <param name="serverCertificate">Server certificate (must include private key).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when the handshake finishes.</returns>
        /// <exception cref="AuthenticationException">Thrown when the TLS handshake fails.</exception>
        internal static async Task AuthenticateServerAsync(
            SslStream sslStream,
            X509Certificate2 serverCertificate,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sslStream);
            ArgumentNullException.ThrowIfNull(serverCertificate);
            SslServerAuthenticationOptions options = new()
            {
                ServerCertificate = serverCertificate,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificateRequired = false,
            };
            await sslStream.AuthenticateAsServerAsync(options, cancellationToken).ConfigureAwait(false);
        }
    }
}
