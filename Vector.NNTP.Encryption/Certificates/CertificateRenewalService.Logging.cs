// <copyright file="CertificateRenewalService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateRenewalService.Logging.cs — Source-generated [LoggerMessage] partial methods for all structured log messages
// emitted by the CertificateRenewalService partial files.
//
// Uses the [LoggerMessage] source generator pattern mandated by CONTRIBUTING.md for compile-time validation,
// zero-allocation logging, and consistent structure.  The built-in IsEnabled guard in each source-generated method
// skips message formatting entirely when the target log level is disabled -- eliminating the need for manual
// logger.IsEnabled() checks at call sites.
//
// Callers (by partial file):
//   CertificateRenewalService.cs                  — (none -- core declaration only)
//   CertificateRenewalService.Lifecycle.cs        — LogConfigurationInvalid, LogAutoRenewalDisabled,
//                                                   LogForcingStagingDirectory, LogStartupRetry, LogRenewalCheckFailed,
//                                                   LogCertificateExpiringSoon, LogNoCertificate,
//                                                   LogNewCertificateActive.
//   CertificateRenewalService.CertificateState.cs — LogCertificateActivated, LogSkippingRenewal, LogWithinThreshold,
//                                                   LogSubscriberException, LogCngKeyCleanupFailed.
//
// Event ID allocation:
//   100-109  Lifecycle -- startup, configuration, environment
//   110-119  Lifecycle -- renewal orchestration (CheckAndRenewAsync)
//   130-139  CertificateState -- validity checks, activation, event, disposal, and CNG key cleanup
//
// Log level policy (aligned with CONTRIBUTING.md Log Levels):
//   Critical     — Fatal configuration errors that prevent startup (EventId 100).
//   Error        — Renewal failures: startup retry (103), steady-state check (104), subscriber exceptions (132).
//   Information  — Operator-visible milestones: auto-renewal disabled (101), no certificate (112), certificate
//                  expiring (111), new certificate active (114), certificate activated on all paths (133).
//   Warning      — Non-standard configuration: staging enforcement (102).
//   Debug        — Diagnostic detail: within-threshold check (131), skipping renewal (130), CNG key cleanup
//                  failure (134).
//
// Security:
//   No method logs private key material, PFX bytes, ACME account keys, or Cloudflare credentials.  Thumbprints are
//   SHA-1 hashes of the public certificate -- not sensitive.  Subject is the certificate's Common Name -- public
//   information visible in any TLS handshake.  NotAfter is the expiry date -- also public information.
//
// ASCII-only log messages:
//   All Message strings contain only ASCII characters (U+0020-U+007E) per CONTRIBUTING.md.  Em-dashes are replaced
//   with " -- ", arrows with " -> ".
//
// SIMD applicability:
//   Not applicable.  This file contains only [LoggerMessage] attribute declarations and XML documentation.  No
//   executable logic, no buffers, no computation.
//
// Cross-platform compatibility:
//   Fully compatible with Linux and Windows (ARM is not required).  [LoggerMessage] source-generated methods use only
//   BCL logging abstractions.  No platform-specific APIs.

using System.Security.Cryptography.X509Certificates;
using Vector.NNTP.Encryption.Configuration;

namespace Vector.NNTP.Encryption.Certificates
{

    internal sealed partial class CertificateRenewalService
    {
        #region Logging Methods — Startup and Configuration (100-109)

        /// <summary>
        /// Logs that the Let's Encrypt configuration is invalid and the service cannot start.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ExecuteAsync"/> — when <see cref="IOptions{TOptions}.Value"/>
        /// throws <see cref="OptionsValidationException"/>.</para>
        ///
        /// <para><b>Impact:</b> <see cref="IHostApplicationLifetime.StopApplication"/> is called immediately after this log
        /// message.  The operator must fix the configuration and restart.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Critical"/> because this is a fatal configuration error that
        /// prevents the service from starting.  The <c>{Failures}</c> parameter contains the semicolon-delimited validation
        /// failure messages from <see cref="OptionsValidationException.Failures"/> — providing
        /// the operator with actionable detail on which configuration properties are invalid.</para>
        /// </remarks>
        [LoggerMessage(EventId = 100, Level = LogLevel.Critical,
            Message = "Certificates: Let's Encrypt configuration is invalid -- the service cannot start. " +
                      "Fix the LetsEncrypt section in appsettings.json and restart. Failures: {Failures}")]
        private partial void LogConfigurationInvalid(string failures);

        /// <summary>
        /// Logs that Let's Encrypt auto-renewal is disabled via configuration.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ExecuteAsync"/> — when <see cref="LetsEncryptOptions.Enabled"/>
        /// is <see langword="false"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> because this is an operator-visible startup
        /// milestone — the service has acknowledged the configuration and will not perform any ACME operations.  The
        /// operator can verify that auto-renewal is intentionally disabled.</para>
        /// </remarks>
        [LoggerMessage(EventId = 101, Level = LogLevel.Information,
            Message = "Certificates: Let's Encrypt auto-renewal is disabled")]
        private partial void LogAutoRenewalDisabled();

        /// <summary>
        /// Logs that the Development environment forces <see cref="LetsEncryptOptions.UseStagingDirectory"/>
        /// to <see langword="true"/> as a safety net against consuming production rate limits.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ExecuteAsync"/> — when <c>hostEnvironment.IsDevelopment()</c> is
        /// <see langword="true"/> and <see cref="LetsEncryptOptions.UseStagingDirectory"/> was
        /// <see langword="false"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Warning"/> because this is a non-standard configuration
        /// override — the operator configured production ACME but the service is forcibly using staging.  Staging
        /// certificates are not publicly trusted, so the operator must be aware that TLS clients will reject them unless
        /// they trust the staging CA.  The warning also provides the remediation path
        /// (<c>DOTNET_ENVIRONMENT=Production</c>).</para>
        /// </remarks>
        [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
            Message = "Certificates: Development environment detected -- forcing UseStagingDirectory=true. " +
                      "Staging certificates are not publicly trusted but have much higher rate limits. " +
                      "Set DOTNET_ENVIRONMENT=Production to use the production ACME directory")]
        private partial void LogForcingStagingDirectory();

        /// <summary>
        /// Logs a concise encryption configuration summary at startup for deployment verification.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ExecuteAsync"/> after options validation and environment enforcement when
        /// <see cref="LetsEncryptOptions.Enabled"/> is true.</para>
        /// <para><b>Security:</b> Emits only public DNS names, mode, cluster flag, node identity, and certificate store
        /// path. Never logs API tokens, signing secrets, account keys, or passwords.</para>
        /// </remarks>
        /// <param name="domain">Comma-separated domain summary.</param>
        /// <param name="mode">Effective ACME directory mode (<c>Production</c> or <c>Staging</c>).</param>
        /// <param name="clusterEnabled">Whether cluster certificate sync is enabled.</param>
        /// <param name="node">Configured node name.</param>
        /// <param name="certificateStore">Certificate storage directory path.</param>
        [LoggerMessage(EventId = 105, Level = LogLevel.Information,
            Message = "Certificates: Encryption initialized -- Domain={Domain} Mode={Mode} ClusterEnabled={ClusterEnabled} Node={Node} CertificateStore={CertificateStore}")]
        private partial void LogEncryptionInitialized(
            string domain,
            string mode,
            bool clusterEnabled,
            string node,
            string certificateStore);

        /// <summary>
        /// Logs a startup retry after a failed certificate acquisition attempt, including the exponential back-off delay.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ExecuteAsync"/> — in the startup retry loop's <c>catch (Exception)</c>
        /// block.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Error"/> because each failed startup attempt means TLS is
        /// unavailable — NNTPS listeners cannot accept connections until a certificate is obtained.  The exception is
        /// passed as the first parameter for full stack-trace logging in file sinks.  The <c>{Attempt}</c> and
        /// <c>{DelaySeconds}</c> parameters enable operators to monitor the retry progression and back-off
        /// behaviour.</para>
        /// </remarks>
        [LoggerMessage(EventId = 103, Level = LogLevel.Error,
            Message = "Certificates: Certificate acquisition failed (attempt {Attempt}) -- retrying in {DelaySeconds}s. " +
                      "TLS is unavailable until a certificate is obtained")]
        private partial void LogStartupRetry(Exception ex, int attempt, int delaySeconds);

        /// <summary>
        /// Logs that a steady-state renewal check failed and will be retried at the next interval.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ExecuteAsync"/> — in the steady-state renewal loop's <c>catch (Exception)</c>
        /// block.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Error"/> because a renewal failure in steady-state means the
        /// current certificate may expire if the failure persists across multiple check intervals.  The <c>{Hours}</c>
        /// parameter shows when the next retry will occur (driven by
        /// <see cref="LetsEncryptOptions.RenewalCheckIntervalHours"/>).  The exception is passed for diagnostic
        /// correlation — typical causes include ACME server unavailability, Cloudflare API errors, and DNS propagation
        /// timeouts.</para>
        /// </remarks>
        [LoggerMessage(EventId = 104, Level = LogLevel.Error,
            Message = "Certificates: Certificate renewal check failed -- will retry in {Hours}h")]
        private partial void LogRenewalCheckFailed(Exception ex, int hours);

        #endregion

        #region Logging Methods — Renewal Orchestration (110-119)

        /// <summary>
        /// Logs that the current certificate expires within the renewal threshold and renewal will be attempted.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="CheckAndRenewAsync"/> — when the certificate's remaining validity is within
        /// <see cref="LetsEncryptOptions.RenewBeforeExpiryDays"/>.  This is an operator-visible milestone at
        /// Information level — distinct from the Debug-level <see cref="LogWithinThreshold"/> emitted by
        /// <see cref="IsCertificateValidBeyondThreshold"/> which provides diagnostic detail.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> because the service is about to initiate a
        /// renewal cycle — the operator should see when and why renewals are triggered.  The <c>{Days}</c> parameter
        /// (formatted to zero decimal places via <c>:F0</c>) shows the remaining certificate validity, and
        /// <c>{Threshold}</c> shows the configured trigger point for context.</para>
        /// </remarks>
        [LoggerMessage(EventId = 111, Level = LogLevel.Information,
            Message = "Certificates: Certificate expires in {Days:F0} days (threshold: {Threshold} days) -- renewing")]
        private partial void LogCertificateExpiringSoon(double days, int threshold);

        /// <summary>
        /// Logs that no valid certificate is present — the service will request one from Let's Encrypt.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="CheckAndRenewAsync"/> — when <see cref="GetCurrentCertificate"/> returns
        /// <see langword="null"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> because this is an operator-visible milestone
        /// indicating the service has no certificate and will attempt to obtain one.  On first startup with no cached PFX,
        /// this is expected; on subsequent runs, it indicates the cached certificate expired or was corrupted.</para>
        /// </remarks>
        [LoggerMessage(EventId = 112, Level = LogLevel.Information,
            Message = "Certificates: No valid certificate -- requesting from Let's Encrypt")]
        private partial void LogNoCertificate();

        /// <summary>
        /// Logs that a new certificate has been activated after a successful ACME renewal.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="CheckAndRenewAsync"/> — after <see cref="ActivateCertificate"/> on the
        /// renewal path.  The logged values are read from the activated certificate via <see cref="GetCurrentCertificate"/>
        /// — safe because the certificate is not disposed until superseded by a future activation.</para>
        ///
        /// <para><b>Distinction from <see cref="LogCertificateActivated"/> (EventId 133):</b> This method is emitted only
        /// on the ACME renewal path in <see cref="CheckAndRenewAsync"/>, providing an operator-visible milestone
        /// that the ACME renewal completed successfully.  <see cref="LogCertificateActivated"/> is the canonical
        /// activation log emitted by <see cref="ActivateCertificate"/> for all activation paths (cache load, ACME
        /// renewal).</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> because this confirms the ACME renewal
        /// completed.  Operators can correlate this log with <see cref="LogCertificateActivated"/> entries to verify
        /// certificate propagation.</para>
        ///
        /// <para><b>Security:</b> <c>{Subject}</c> is the certificate's Common Name (public information visible in any TLS
        /// handshake).  <c>{Thumbprint}</c> is a SHA-1 hash of the public certificate — not sensitive.
        /// <c>{NotAfter}</c> is the expiry date — public information.</para>
        /// </remarks>
        [LoggerMessage(EventId = 114, Level = LogLevel.Information,
            Message = "Certificates: New certificate active: Subject={Subject}, Thumbprint={Thumbprint}, Expires={NotAfter:yyyy/MM/dd HH:mm:ss}")]
        private partial void LogNewCertificateActive(string subject, string thumbprint, DateTime notAfter);

        /// <summary>
        /// Logs that cluster sync was requested but MessageBus services are not registered.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ExecuteAsync"/> -- when <see cref="LetsEncryptOptions.ClusterEnabled"/>
        /// is <see langword="true"/> but RabbitMQ DI services are missing.</para>
        /// </remarks>
        [LoggerMessage(EventId = 115, Level = LogLevel.Warning,
            Message = "Certificates: LetsEncrypt ClusterEnabled is true but MessageBus services are not registered; cluster sync is disabled")]
        private partial void LogClusterSyncDisabled();

        #endregion

        #region Logging Methods — Certificate State (130-139)

        /// <summary>
        /// Logs that the certificate is still valid beyond the renewal threshold — skipping renewal.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="IsCertificateValidBeyondThreshold"/> — when the remaining validity exceeds the
        /// threshold.  Called from the entry gate in <see cref="CheckAndRenewAsync"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Debug"/> because this is the expected happy-path outcome on
        /// every periodic check cycle — the certificate is still valid and no action is needed.  Emitted frequently
        /// (every <see cref="LetsEncryptOptions.RenewalCheckIntervalHours"/>) and only useful for diagnosing why a renewal
        /// did or did not occur.  The <c>{Days}</c> parameter (formatted to zero decimal places via <c>:F0</c>) shows
        /// the remaining validity.</para>
        /// </remarks>
        [LoggerMessage(EventId = 130, Level = LogLevel.Debug,
            Message = "Certificates: Certificate valid for {Days:F0} more days -- skipping renewal")]
        private partial void LogSkippingRenewal(double days);

        /// <summary>
        /// Logs that the certificate expires within the renewal threshold.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="IsCertificateValidBeyondThreshold"/> — when the remaining validity is within
        /// the threshold.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Debug"/> because this is a diagnostic detail emitted by the
        /// validity-check helper — the operator-visible Information-level milestone is provided by
        /// <see cref="LogCertificateExpiringSoon"/> (EventId 111) in the caller (<see cref="CheckAndRenewAsync"/>).
        /// The <c>{Days}</c> parameter (formatted to zero decimal places via <c>:F0</c>) shows the remaining validity
        /// from the helper's perspective.</para>
        /// </remarks>
        [LoggerMessage(EventId = 131, Level = LogLevel.Debug,
            Message = "Certificates: Certificate expires within threshold ({Days:F0} days remaining)")]
        private partial void LogWithinThreshold(double days);

        /// <summary>
        /// Logs that a <see cref="ICertificateRenewalPublisher.CertificateChanged"/> event subscriber threw an exception.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="RaiseCertificateChanged"/> — in the per-subscriber <c>catch (Exception)</c>
        /// block.  The faulting subscriber does not prevent remaining subscribers from being notified.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Error"/> because a subscriber exception indicates a callback
        /// failure (per CONTRIBUTING.md Log Levels) — a component that depends on certificate updates (e.g.
        /// <c>NntpListener</c> failing to bind the TLS socket) has malfunctioned.  The exception is passed
        /// as the first parameter for full stack-trace logging.  Per-subscriber isolation ensures subsequent subscribers
        /// are still notified and <see cref="DeferCertificateDisposal"/> always executes.</para>
        /// </remarks>
        [LoggerMessage(EventId = 132, Level = LogLevel.Error,
            Message = "Certificates: CertificateChanged subscriber threw an exception")]
        private partial void LogSubscriberException(Exception ex);

        /// <summary>
        /// Logs that a certificate has been activated (atomic swap completed).  Emitted for all activation paths: cached
        /// certificate load and ACME renewal.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ActivateCertificate"/> — immediately after the atomic swap, before event
        /// notification and deferred disposal.  This is the single canonical log for all certificate activations —
        /// distinct from <see cref="LogNewCertificateActive"/> (EventId 114) which is emitted only on the ACME renewal
        /// path in <see cref="CheckAndRenewAsync"/>.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Information"/> because certificate activation is an
        /// operator-visible milestone — the current TLS certificate has changed and all subsequent handshakes will use the
        /// new certificate.</para>
        ///
        /// <para><b>Security:</b> <c>{Subject}</c> is the certificate's Common Name (public information visible in any TLS
        /// handshake).  <c>{Thumbprint}</c> is a SHA-1 hash of the public certificate — not sensitive.
        /// <c>{NotAfter}</c> is the expiry date — public information.</para>
        /// </remarks>
        [LoggerMessage(EventId = 133, Level = LogLevel.Information,
            Message = "Certificates: Certificate activated -- Subject={Subject}, Thumbprint={Thumbprint}, Expires={NotAfter:yyyy/MM/dd HH:mm:ss}")]
        private partial void LogCertificateActivated(string subject, string thumbprint, DateTime notAfter);

        /// <summary>
        /// Logs that synchronous CNG private key cleanup failed during certificate rotation.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="CleanupCngKeyImmediately"/> — in the <c>catch (Exception)</c> block after
        /// <see cref="ECDsaCertificateExtensions.GetECDsaPrivateKey(X509Certificate2)"/> or
        /// <see cref="RSACertificateExtensions.GetRSAPrivateKey(X509Certificate2)"/> throws during
        /// key handle retrieval or disposal.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Debug"/> because the most common cause is the expected
        /// double-cleanup race: <c>NntpSocketAcceptor.OnCertificateChanged</c> may call
        /// <see cref="CertificateStore.DeferDisposal"/> on the same certificate, and if its deferred disposal fires
        /// first, the CNG key is already deleted.  The subsequent <c>GetECDsaPrivateKey()</c> call here throws
        /// <see cref="System.Security.Cryptography.CryptographicException"/> ("The system cannot find the file
        /// specified") — a harmless, expected condition.  The key was already cleaned up; no orphan remains.</para>
        /// </remarks>
        [LoggerMessage(EventId = 134, Level = LogLevel.Debug,
            Message = "Certificates: Synchronous CNG key cleanup failed during certificate rotation -- key may already be deleted")]
        private partial void LogCngKeyCleanupFailed(Exception ex);

        #endregion
    }

}
