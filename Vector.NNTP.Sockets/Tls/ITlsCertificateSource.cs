// <copyright file="ITlsCertificateSource.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: TLS certificate material supplied by hosts (Encryption assembly later).

namespace Vector.NNTP.Sockets.Tls
{
    /// <summary>
    /// Supplies server TLS certificate for STARTTLS and implicit TLS listeners.
    /// </summary>
    public interface ITlsCertificateSource
    {
        /// <summary>
        /// Gets the server certificate for SslStream authentication.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Certificate instance or null when TLS is not configured.</returns>
        ValueTask<System.Security.Cryptography.X509Certificates.X509Certificate2?> GetServerCertificateAsync(CancellationToken cancellationToken);
    }
}
