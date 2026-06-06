// <copyright file="CertificateKeyUtilities.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateKeyUtilities.cs -- CSR and PFX helpers for ACME certificate finalisation.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;

namespace Vector.NNTP.Encryption.Certificates.Acme
{
    /// <summary>
    /// Cryptographic helpers for ACME CSR generation and PKCS#12 (PFX) construction.
    /// </summary>
    /// <remarks>
    /// <para><b>Scope:</b> These helpers are designed for ACME certificate provisioning flows that already use
    /// <see cref="IKey"/> and <see cref="CertificateChain"/> from Certes.</para>
    ///
    /// <para><b>Thread safety:</b> All methods are <see langword="static"/> and stateless. Safe for concurrent use from
    /// any thread without synchronisation.</para>
    ///
    /// <para><b>Allocation:</b> Methods allocate byte arrays and certificate objects as required by the BCL export APIs;
    /// not intended for hot-path per-packet use.</para>
    /// </remarks>
    internal static class CertificateKeyUtilities
    {
        /// <summary>
        /// Imports a Certes <see cref="IKey"/> into a new <see cref="ECDsa"/> instance.
        /// </summary>
        /// <param name="certesKey">Certes private key to import.</param>
        /// <returns>A new <see cref="ECDsa"/> instance containing the imported key material.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="certesKey"/> is <see langword="null"/>.</exception>
        /// <exception cref="CryptographicException">Thrown when the key bytes are invalid.</exception>
        internal static ECDsa ImportEcdsaPrivateKey(IKey certesKey)
        {
            ArgumentNullException.ThrowIfNull(certesKey);

            ECDsa ecdsa = ECDsa.Create();

            try
            {
                ecdsa.ImportPkcs8PrivateKey(certesKey.ToDer(), out _);
                return ecdsa;
            }
            catch
            {
                ecdsa.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Builds a PKCS#12 (PFX) archive from an ACME certificate chain and private key.
        /// </summary>
        /// <param name="chain">Certificate chain returned by the ACME order download.</param>
        /// <param name="privateKey">Certificate private key.</param>
        /// <param name="password">Optional PFX password; pass <see langword="null"/> for an unprotected export.</param>
        /// <returns>PFX bytes suitable for persistence.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="chain"/> or <paramref name="privateKey"/> is
        /// <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when export returns <see langword="null"/>.</exception>
        internal static byte[] BuildPfxFromChain(CertificateChain chain, IKey privateKey, string? password = null)
        {
            ArgumentNullException.ThrowIfNull(chain);
            ArgumentNullException.ThrowIfNull(privateKey);

            using ECDsa ecdsa = ImportEcdsaPrivateKey(privateKey);

            using X509Certificate2 leafCert = new(chain.Certificate.ToDer(), (string?)null, X509KeyStorageFlags.Exportable);
            using X509Certificate2 leafWithKey = leafCert.CopyWithPrivateKey(ecdsa);

            X509Certificate2Collection exportCollection = [leafWithKey];

            try
            {
                foreach (IEncodable issuer in chain.Issuers)
                {
                    _ = exportCollection.Add(new X509Certificate2(issuer.ToDer()));
                }

                return exportCollection.Export(X509ContentType.Pfx, password)
                    ?? throw new InvalidOperationException("X509Certificate2Collection.Export returned null for a non-empty PFX collection.");
            }
            finally
            {
                for (int i = 1; i < exportCollection.Count; i++)
                {
                    exportCollection[i].Dispose();
                }
            }
        }

        /// <summary>
        /// Builds a DER-encoded PKCS#10 CSR for the given domain names.
        /// </summary>
        /// <param name="domainNames">DNS names to include as SANs.</param>
        /// <param name="privateKey">Certificate private key.</param>
        /// <returns>DER-encoded CSR bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainNames"/> or <paramref name="privateKey"/> is
        /// <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="domainNames"/> is empty.</exception>
        internal static byte[] CreateCsr(string[] domainNames, IKey privateKey)
        {
            ArgumentNullException.ThrowIfNull(domainNames);
            ArgumentNullException.ThrowIfNull(privateKey);

            if (domainNames.Length == 0)
            {
                throw new ArgumentException("At least one domain name is required to build a CSR.", nameof(domainNames));
            }

            using ECDsa ecdsa = ImportEcdsaPrivateKey(privateKey);

            X500DistinguishedNameBuilder subjectBuilder = new();
            subjectBuilder.AddCommonName(domainNames[0]);

            CertificateRequest csr = new(
                subjectBuilder.Build(),
                ecdsa,
                HashAlgorithmName.SHA256);

            csr.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));

            csr.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    [new Oid("1.3.6.1.5.5.7.3.1", "TLS Web Server Authentication")],
                    critical: false));

            SubjectAlternativeNameBuilder sanBuilder = new();
            foreach (string domain in domainNames)
            {
                sanBuilder.AddDnsName(domain);
            }

            csr.CertificateExtensions.Add(sanBuilder.Build());

            return csr.CreateSigningRequest();
        }
    }
}
