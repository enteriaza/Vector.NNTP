// <copyright file="LetsEncryptOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// LetsEncryptOptions.cs -- Strongly-typed configuration for ACME DNS-01 certificate provisioning.
//
// Bound from the LetsEncrypt section by the host via IOptions. Property names align with NNRPD.json / NNTPD.json.

using System.ComponentModel.DataAnnotations;
using Certes;

namespace Vector.NNTP.Encryption.Configuration
{
    /// <summary>
    /// Configuration for automatic Let's Encrypt certificate provisioning via ACME DNS-01 with Cloudflare DNS.
    /// </summary>
    /// <remarks>
    /// <para>When <see cref="Enabled"/> is <see langword="false"/>, the certificate renewal hosted service exits
    /// immediately and no ACME or filesystem work is performed.</para>
    ///
    /// <para>Enhancement properties (DNS quorum, clock skew, transient retry, jitter, cluster sync) are consumed by
    /// <see cref="Certificates.Acme.AcmeCertificateProvider"/> and <see cref="Certificates.CertificateRenewalService"/>.</para>
    /// </remarks>
    public sealed class LetsEncryptOptions
    {
        /// <summary>
        /// Configuration section name used by hosts when binding these options.
        /// </summary>
        public const string SectionName = "LetsEncrypt";

        /// <summary>
        /// Filename for the ACME account private key (PEM-encoded).
        /// </summary>
        public const string AccountKeyFileName = "letsencrypt.pem";

        /// <summary>
        /// Filename for the cached TLS certificate (PKCS#12).
        /// </summary>
        public const string CertificateFileName = "certificate.pfx";

        /// <summary>
        /// Filename for the certificate private key (PEM-encoded, ES256).
        /// </summary>
        public const string CertificateKeyFileName = "certificate-key.pem";

        /// <summary>
        /// Master switch for automatic certificate provisioning.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Directory where issued certificates and keys are stored. Replaces legacy NodeId:DirCerts.
        /// </summary>
        [Required]
        public string CertDir { get; set; } = string.Empty;

        /// <summary>
        /// ACME account contact email (JSON key <c>AcmeAccountEmail</c>).
        /// </summary>
        [Required]
        public string AcmeAccountEmail { get; set; } = string.Empty;

        /// <summary>
        /// PEM-encoded ACME account private key shared across cluster nodes.
        /// </summary>
        public string AccountKeyPem { get; set; } = string.Empty;

        /// <summary>
        /// Domain names (SANs) requested on the ACME order.
        /// </summary>
        public string[] DomainNames { get; set; } = [];

        /// <summary>
        /// Use the Let's Encrypt staging ACME directory when <see langword="true"/>.
        /// </summary>
        public bool UseStagingDirectory { get; set; }

        /// <summary>
        /// Hours between steady-state renewal checks.
        /// </summary>
        [Range(1, 168)]
        public int RenewalCheckIntervalHours { get; set; } = 6;

        /// <summary>
        /// Days before expiry when renewal is triggered.
        /// </summary>
        [Range(1, 60)]
        public int RenewBeforeExpiryDays { get; set; } = 14;

        /// <summary>
        /// Cloudflare API token with Zone:DNS:Edit permission.
        /// </summary>
        public string CloudflareApiToken { get; set; } = string.Empty;

        /// <summary>
        /// Cloudflare zone identifier for DNS-01 TXT records.
        /// </summary>
        public string CloudflareZoneId { get; set; } = string.Empty;

        /// <summary>
        /// Maximum attempts for transient ACME retries (new order, finalize, download).
        /// </summary>
        [Range(1, 20)]
        public int AcmeTransientRetryMaxAttempts { get; set; } = 5;

        /// <summary>
        /// Maximum tolerated clock skew in minutes against the ACME directory Date header.
        /// </summary>
        [Range(1, 60)]
        public int ClockSkewMaxMinutes { get; set; } = 10;

        /// <summary>
        /// Minutes to reuse a successful clock-skew check before re-querying the directory.
        /// </summary>
        [Range(1, 60)]
        public int ClockSkewCheckTtlMinutes { get; set; } = 5;

        /// <summary>
        /// Seconds to wait after publishing DNS TXT records before the first authoritative poll.
        /// </summary>
        [Range(0, 300)]
        public int DnsPropagationDelaySeconds { get; set; } = 15;

        /// <summary>
        /// Seconds between authoritative TXT quorum polls.
        /// </summary>
        [Range(1, 60)]
        public int DnsTxtPollIntervalSeconds { get; set; } = 3;

        /// <summary>
        /// Maximum seconds to wait for TXT propagation across challenge names.
        /// </summary>
        [Range(5, 3600)]
        public int DnsTxtPollTimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// Fraction of authoritative name servers that must return the expected TXT (0.5–1.0).
        /// </summary>
        [Range(0.5, 1.0)]
        public double DnsAuthoritativeQuorumRatio { get; set; } = 0.7;

        /// <summary>
        /// Minutes to cache authoritative NS address lists per zone.
        /// </summary>
        [Range(1, 60)]
        public int DnsAuthoritativeNsCacheMinutes { get; set; } = 5;

        /// <summary>
        /// Renewal window jitter ratio (0–0.5) applied around scheduled renewal instants.
        /// </summary>
        [Range(0, 0.5)]
        public double RenewalJitterRatio { get; set; } = 0.1;

        /// <summary>
        /// Certificate order shape for ACME orders.
        /// </summary>
        public CertificateOrderMode OrderMode { get; set; } = CertificateOrderMode.WildcardOnly;

        /// <summary>
        /// Optional PFX export password. Never log this value.
        /// </summary>
        public string? PfxExportPassword { get; set; }

        /// <summary>
        /// Enables RabbitMQ fanout sync and leader-only ACME issuance across cluster nodes.
        /// </summary>
        public bool ClusterEnabled { get; set; }

        /// <summary>
        /// Durable fanout exchange name prefix for cluster certificate broadcast.
        /// </summary>
        public string ClusterBroadcastExchange { get; set; } = "vectornntp.certificates.broadcast";

        /// <summary>
        /// Optional HMAC signing secret for cluster certificate payloads. Never log this value.
        /// </summary>
        public string? ClusterBroadcastSigningSecret { get; set; }

        /// <summary>
        /// Previous HMAC signing secret accepted during secret rotation. Never log this value.
        /// </summary>
        public string? ClusterBroadcastSigningSecretPrevious { get; set; }

        /// <summary>
        /// Leader lease duration in seconds for cluster coordination (reserved for future lease semantics).
        /// </summary>
        [Range(5, 3600)]
        public int ClusterLeaseSeconds { get; set; } = 60;

        /// <summary>
        /// Trims and validates <see cref="AccountKeyPem"/> for structural PEM correctness.
        /// </summary>
        /// <param name="errors">Validation errors to append when the key is missing or invalid.</param>
        /// <remarks>
        /// Called from <see cref="LetsEncryptOptionsValidator"/> during host startup validation and referenced by
        /// ACME account bootstrap for account-key invariants.
        /// </remarks>
        public void NormaliseAndValidateAccountKeyPem(List<ValidationResult> errors)
        {
            AccountKeyPem = AccountKeyPem?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(AccountKeyPem))
            {
                errors.Add(new ValidationResult(
                    "AccountKeyPem is required when Let's Encrypt is enabled.",
                    [nameof(AccountKeyPem)]));
                return;
            }

            if (!AccountKeyPem.StartsWith("-----BEGIN", StringComparison.Ordinal))
            {
                errors.Add(new ValidationResult(
                    "AccountKeyPem does not appear to be a valid PEM-encoded key.",
                    [nameof(AccountKeyPem)]));
                return;
            }

            try
            {
                KeyFactory.FromPem(AccountKeyPem);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                errors.Add(new ValidationResult(
                    $"AccountKeyPem contains an invalid private key: {ex.Message}",
                    [nameof(AccountKeyPem)]));
            }
        }
    }

    /// <summary>
    /// Controls how domains are grouped into a single ACME order.
    /// </summary>
    public enum CertificateOrderMode
    {
        /// <summary>
        /// Request a wildcard certificate only (for example <c>*.example.com</c>).
        /// </summary>
        WildcardOnly = 0,

        /// <summary>
        /// Request a wildcard and an explicit hostname on the same order.
        /// </summary>
        WildcardAndHostname = 1,

        /// <summary>
        /// Request a certificate for a single hostname or SAN list without wildcard semantics.
        /// </summary>
        SingleHostname = 2,
    }
}
