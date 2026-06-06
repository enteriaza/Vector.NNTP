// <copyright file="ICertificateRenewalPublisher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: public bridge for TLS certificate hot reload without exposing implementation types.

using System.Security.Cryptography.X509Certificates;

namespace Vector.NNTP.Encryption.Certificates
{
    /// <summary>
    /// Supplies the current TLS certificate and notifies subscribers when a new certificate is activated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ownership contract:</b> Subscribers receive the <em>new</em> certificate on
    /// <see cref="CertificateChanged"/> and must atomically swap their local reference. They must <b>not</b> dispose
    /// the certificate they replace — <see cref="CertificateRenewalService"/> owns superseded certificate disposal.
    /// </para>
    /// <para>
    /// <b>Thread safety:</b> <see cref="GetCurrentCertificate"/> is safe for concurrent reads from TLS handshake paths.
    /// </para>
    /// </remarks>
    public interface ICertificateRenewalPublisher
    {
        /// <summary>
        /// Returns the current TLS certificate, or <see langword="null"/> when none has been provisioned yet.
        /// </summary>
        /// <returns>Active server certificate or <see langword="null"/>.</returns>
        public X509Certificate2? GetCurrentCertificate();

        /// <summary>
        /// Fired when a new certificate is activated (loaded from disk or renewed via ACME).
        /// </summary>
        /// <remarks>
        /// Per-subscriber exceptions are isolated so a faulting subscriber cannot break renewal.
        /// </remarks>
        public event Action<X509Certificate2>? CertificateChanged;
    }
}
