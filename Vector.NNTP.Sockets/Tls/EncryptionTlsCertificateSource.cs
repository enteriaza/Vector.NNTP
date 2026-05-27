// <copyright file="EncryptionTlsCertificateSource.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: bridges Vector.NNTP.Encryption certificate renewal to ITlsCertificateSource.

namespace Vector.NNTP.Sockets.Tls
{
    using Vector.NNTP.Encryption.Certificates;

    /// <summary>
    /// Supplies the current TLS certificate from <see cref="CertificateRenewalService"/>.
    /// </summary>
    internal sealed class EncryptionTlsCertificateSource : ITlsCertificateSource
    {
        private readonly CertificateRenewalService _renewalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="EncryptionTlsCertificateSource"/> class.
        /// </summary>
        /// <param name="renewalService">Certificate renewal hosted service.</param>
        public EncryptionTlsCertificateSource(CertificateRenewalService renewalService)
        {
            this._renewalService = renewalService ?? throw new ArgumentNullException(nameof(renewalService));
        }

        /// <inheritdoc />
        public ValueTask<System.Security.Cryptography.X509Certificates.X509Certificate2?> GetServerCertificateAsync(
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(this._renewalService.GetCurrentCertificate());
        }
    }
}
