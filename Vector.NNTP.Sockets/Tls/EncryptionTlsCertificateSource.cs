// <copyright file="EncryptionTlsCertificateSource.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: bridges Vector.NNTP.Encryption certificate renewal to ITlsCertificateSource.

using Vector.NNTP.Encryption.Certificates;

namespace Vector.NNTP.Sockets.Tls
{
    /// <summary>
    /// Supplies the current TLS certificate from <see cref="CertificateRenewalService"/>.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="EncryptionTlsCertificateSource"/> class.
    /// </remarks>
    /// <param name="renewalService">Certificate renewal hosted service.</param>
    internal sealed class EncryptionTlsCertificateSource(CertificateRenewalService renewalService) : ITlsCertificateSource
    {
        private readonly CertificateRenewalService _renewalService = renewalService ?? throw new ArgumentNullException(nameof(renewalService));

        /// <inheritdoc />
        public ValueTask<System.Security.Cryptography.X509Certificates.X509Certificate2?> GetServerCertificateAsync(
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(_renewalService.GetCurrentCertificate());
        }
    }
}
