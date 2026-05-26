// <copyright file="ClusterCertificatePayload.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Encryption.Cluster
{
    /// <summary>
    /// Logical payload broadcast to cluster followers after a successful ACME issuance.
    /// </summary>
    internal sealed class ClusterCertificatePayload
    {
        /// <summary>
        /// Current wire version for <see cref="Signature"/> canonicalization.
        /// </summary>
        public const int CurrentSignatureVersion = 1;

        /// <summary>
        /// Monotonic fencing epoch; stale leaders must not overwrite newer state.
        /// </summary>
        public long Epoch { get; set; }

        /// <summary>
        /// Canonicalization version included in the signed material.
        /// </summary>
        public int SignatureVersion { get; set; } = CurrentSignatureVersion;

        /// <summary>
        /// PKCS#12 archive as Base64 (password matches local <see cref="Configuration.LetsEncryptOptions.PfxExportPassword"/>).
        /// </summary>
        public string PfxBase64 { get; set; } = string.Empty;

        /// <summary>
        /// SHA-256 fingerprint over the DER-encoded leaf certificate, as 64-character hex.
        /// </summary>
        public string Sha256Thumbprint { get; set; } = string.Empty;

        /// <summary>
        /// DNS names on the ACME order for this certificate.
        /// </summary>
        public string[] Domains { get; set; } = [];

        /// <summary>
        /// Certificate <see cref="System.Security.Cryptography.X509Certificates.X509Certificate2.NotAfter"/> as UTC ticks.
        /// </summary>
        public long NotAfterUtcTicks { get; set; }

        /// <summary>
        /// UTC instant when the leader published this payload.
        /// </summary>
        public long IssuedAtUtcTicks { get; set; }

        /// <summary>
        /// Hex-encoded HMAC-SHA256 over a canonical form of the other fields when a signing secret is configured.
        /// </summary>
        public string Signature { get; set; } = string.Empty;
    }
}
