// <copyright file="AcmeCertificateProvider.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AcmeCertificateProvider.Logging.cs — [LoggerMessage] source-generated partial methods for all structured log messages
// emitted by the AcmeCertificateProvider partial files.
//
// Centralised here to satisfy CA1848 and avoid per-call string formatting, enum boxing, and params object[]
// allocation on hot paths (DNS polling loops, challenge validation retries).
//
// All methods are source-generated at compile time by the [LoggerMessage] attribute.  The compiler emits strongly-typed
// state structs per method, eliminating reflection and allocation at runtime.  Thread-safe for concurrent invocation
// from any thread without synchronisation.
//
// Event ID allocation (contiguous within each range, no collisions):
//   200-209  AcmeAccount                (AcmeCertificateProvider.AcmeAccount.cs)
//   210-219  ChallengeOrchestration     (AcmeCertificateProvider.ChallengeOrchestration.cs)
//   220-229  OrderFinalisation          (AcmeCertificateProvider.OrderFinalisation.cs)
//   230-249  CloudflareDns              (AcmeCertificateProvider.CloudflareDns.cs)
//   250-259  RequestCertificateAsync    (AcmeCertificateProvider.cs -- primary partial)
//
// Naming convention:
//   Each method name matches the nameof() used in its EventId, enabling grep-based correlation between
//   log output (which includes the EventId name) and the source code definition.
//
// Log level policy:
//   Information  -- Operator-visible milestones: account creation, certificate key generation, challenge validated,
//                   nameserver resolution, TXT record visibility, request start.
//   Warning      -- Recoverable degradation: corrupt key files, nameserver resolution failure, propagation timeout,
//                   Cloudflare API failure, cleanup failure.
//   Debug        -- Diagnostic detail: polling status, cached client reuse, ACME directory URI, individual DNS
//                   query failures.  Guarded by logger.IsEnabled(LogLevel.Debug) at call sites to avoid
//                   allocation (Uri.ToString, string.Join) when Debug is disabled.
//
// Security:
//   No method logs credentials (API tokens, PEM key content, zone IDs).  The email address logged by
//   LogCreatedNewAcmeAccount is the operator-configured contact email, expected in diagnostic logs.  The TXT
//   record value logged by LogSettingDnsTxtRecord is a transient, one-use base64url hash -- not a secret.
//
// ASCII-only:
//   All Message strings contain only ASCII characters (U+0020-U+007E) per CONTRIBUTING.md.  Unicode characters
//   (em-dash, arrows, etc.) are replaced with their ASCII equivalents (--,  ->, etc.).

using Certes.Acme.Resource;
using Vector.NNTP.Encryption.Acme;
using Vector.NNTP.Encryption.Dns;

namespace Vector.NNTP.Encryption.Certificates.Acme
{

    internal sealed partial class AcmeCertificateProvider
    {
        #region Logging Methods -- ACME Account (200-209)

        /// <summary>
        /// Logs that an existing ACME account was verified with the server using the configuration-provided key.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="LoadOrCreateAccountAsync"/> -- after <c>acme.Account()</c> succeeds.
        /// Guarded by <c>logger.IsEnabled(LogLevel.Debug)</c>.</para>
        /// </remarks>
        [LoggerMessage(EventId = 200, Level = LogLevel.Debug,
            Message = "Certificates: Loaded existing ACME account from {Source}")]
        private partial void LogLoadedExistingAcmeAccount(string source);

        /// <summary>
        /// Logs that a new ACME account was registered with the server using the configuration-provided key.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="LoadOrCreateAccountAsync"/> -- after <c>NewAccount</c> succeeds.  The
        /// <c>{Email}</c> parameter is the operator-configured contact email from
        /// <see cref="Configuration.LetsEncryptOptions.AcmeAccountEmail"/>.</para>
        /// </remarks>
        [LoggerMessage(EventId = 201, Level = LogLevel.Information,
            Message = "Certificates: Created new ACME account for {Email}, key sourced from {Source}")]
        private partial void LogCreatedNewAcmeAccount(string email, string source);

        /// <summary>
        /// Logs that the configuration-provided ACME account key is not registered with the ACME server and a new account
        /// will be created using the same key.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="LoadOrCreateAccountAsync"/> -- in the <c>catch</c> block when
        /// <c>acme.Account()</c> throws (account not found on the ACME server).  The original exception is passed as the
        /// <see cref="Exception"/> parameter for diagnostic correlation.  This is expected on first use of a key or when
        /// switching between staging and production directories.</para>
        /// </remarks>
        [LoggerMessage(EventId = 202, Level = LogLevel.Information,
            Message = "Certificates: Account key from configuration is not registered with the ACME server -- registering new account with the same key")]
        private partial void LogAccountKeyNotRegistered(Exception ex);

        /// <summary>
        /// Logs the ACME directory URI being used for account operations (staging or production).
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="LoadOrCreateAccountAsync"/> -- immediately after resolving the directory URI.
        /// Guarded by <c>logger.IsEnabled(LogLevel.Debug)</c> to avoid <see cref="Uri.ToString"/> allocation when
        /// Debug is disabled.</para>
        /// </remarks>
        [LoggerMessage(EventId = 203, Level = LogLevel.Debug,
            Message = "Certificates: Using ACME directory {DirectoryUri}")]
        private partial void LogUsingAcmeDirectory(Uri directoryUri);

        /// <summary>
        /// Logs that the ACME directory clock skew check failed and will be retried once.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="AssertClockSkewIfNeededAsync"/> -- on the first
        /// <see cref="InvalidOperationException"/> from <see cref="ClockSkewGuard.AssertSkewAcceptableAsync"/>.</para>
        /// </remarks>
        [LoggerMessage(EventId = 204, Level = LogLevel.Warning,
            Message = "Certificates: ACME directory clock skew check failed; retrying once after a short delay")]
        private partial void LogClockSkewCheckRetry(Exception ex);

        #endregion

        #region Logging Methods -- Challenge Orchestration (210-219)

        /// <summary>
        /// Logs that a DNS TXT record is being created for an ACME challenge.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="RequestCertificateAsync"/> -- before calling
        /// <see cref="CreateCloudflareTxtRecordAsync"/>.  The <c>{Value}</c> parameter is the base64url-encoded ACME
        /// challenge digest -- a transient, one-use hash, not a secret.</para>
        /// </remarks>
        [LoggerMessage(EventId = 210, Level = LogLevel.Information,
            Message = "Certificates: Setting DNS TXT record: {Name} = {Value}")]
        private partial void LogSettingDnsTxtRecord(string name, string value);

        /// <summary>
        /// Logs that a DNS-01 challenge has been successfully validated for a domain.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="RequestCertificateAsync"/> -- after <see cref="ValidateChallengeAsync"/>
        /// returns successfully (challenge reached <see cref="ChallengeStatus.Valid"/>).</para>
        /// </remarks>
        [LoggerMessage(EventId = 212, Level = LogLevel.Information,
            Message = "Certificates: DNS-01 challenge validated for {Domain}")]
        private partial void LogChallengeValidated(string domain);

        /// <summary>
        /// Logs the current DNS-01 challenge polling status for a domain.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="ValidateChallengeAsync"/> -- on each poll iteration while waiting for the
        /// challenge to reach a terminal status.  Guarded by <c>logger.IsEnabled(LogLevel.Debug)</c>.  The
        /// <see cref="ChallengeStatus"/> nullable enum is handled by the generic state struct without boxing on
        /// .NET 8.</para>
        /// </remarks>
        [LoggerMessage(EventId = 213, Level = LogLevel.Debug,
            Message = "Certificates: DNS-01 challenge for {Domain}: Status={Status}, attempt {Attempt}/{Max}")]
        private partial void LogChallengePollingStatus(string domain, ChallengeStatus status, int attempt, int max);

        #endregion

        #region Logging Methods -- Order Finalisation (220-229)

        /// <summary>
        /// Logs the ACME order status and URL after finalization.  The order URL
        /// (e.g. <c>https://acme-v02.api.letsencrypt.org/acme/order/123456789/987654321</c>) enables direct lookup in
        /// Let's Encrypt logs and the ACME server's order management API for debugging.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="FinaliseOrderAsync"/> -- after <c>order.Finalize(csrBytes)</c> returns.
        /// Guarded by <c>logger.IsEnabled(LogLevel.Debug)</c> to avoid <see cref="Uri.ToString"/> allocation.</para>
        /// </remarks>
        [LoggerMessage(EventId = 220, Level = LogLevel.Debug,
            Message = "Certificates: ACME order finalized, status is {Status}, order={OrderUrl}")]
        private partial void LogOrderFinalized(OrderStatus status, Uri orderUrl);

        /// <summary>
        /// Logs that the ACME order has reached an accepted status.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="WaitForOrderStatusAsync"/> -- when the order status matches one of the
        /// accepted statuses.  Guarded by <c>logger.IsEnabled(LogLevel.Debug)</c>.</para>
        /// </remarks>
        [LoggerMessage(EventId = 221, Level = LogLevel.Debug,
            Message = "Certificates: ACME order status is {Status} -- {Suffix}")]
        private partial void LogOrderStatusAccepted(OrderStatus status, string suffix);

        /// <summary>
        /// Logs the current ACME order polling status while waiting for an expected status.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="WaitForOrderStatusAsync"/> -- on each poll iteration.  Guarded by
        /// <c>logger.IsEnabled(LogLevel.Debug)</c>.</para>
        /// </remarks>
        [LoggerMessage(EventId = 222, Level = LogLevel.Debug,
            Message = "Certificates: ACME order status is {Status}, waiting for {Expected} (attempt {Attempt}/{Max})")]
        private partial void LogOrderPollingStatus(OrderStatus status, string expected, int attempt, int max);

        /// <summary>
        /// Logs that an existing certificate key was loaded from disk.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="LoadOrCreateCertificateKeyAsync"/> -- after
        /// <see cref="Certes.KeyFactory.FromPem"/> succeeds.  Guarded by
        /// <c>logger.IsEnabled(LogLevel.Debug)</c>.</para>
        /// </remarks>
        [LoggerMessage(EventId = 223, Level = LogLevel.Debug,
            Message = "Certificates: Loaded existing certificate key from {Path}")]
        private partial void LogLoadedExistingCertificateKey(string path);

        /// <summary>
        /// Logs that a new ES256 certificate key was generated and persisted to disk.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="LoadOrCreateCertificateKeyAsync"/> -- after
        /// <see cref="Certes.KeyFactory.NewKey"/> and <see cref="CertificateStore.SaveCertificateKeyAsync"/> both
        /// succeed.</para>
        /// </remarks>
        [LoggerMessage(EventId = 224, Level = LogLevel.Information,
            Message = "Certificates: Generated new ES256 certificate key, saved to {Path}")]
        private partial void LogGeneratedNewCertificateKey(string path);

        /// <summary>
        /// Logs that the certificate private key file was present but contained invalid PEM data.  The corrupt key will
        /// be replaced by a freshly generated ES256 key.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="LoadOrCreateCertificateKeyAsync"/> -- in the <c>catch</c> block when
        /// <see cref="Certes.KeyFactory.FromPem"/> throws.  The original exception is passed as the
        /// <see cref="Exception"/> parameter for diagnostic correlation.</para>
        /// </remarks>
        [LoggerMessage(EventId = 225, Level = LogLevel.Warning,
            Message = "Certificates: Certificate key at {Path} is corrupt -- generating new key")]
        private partial void LogCertificateKeyCorrupt(Exception ex, string path);

        #endregion

        #region Logging Methods -- Cloudflare DNS (230-249)

        /// <summary>
        /// Logs that a Cloudflare TXT record was successfully created.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="CreateCloudflareTxtRecordAsync"/> -- after the Cloudflare
        /// <c>POST /dns_records</c> API returns successfully.  Guarded by <c>logger.IsEnabled(LogLevel.Debug)</c>.
        /// The <c>{Id}</c> parameter is the Cloudflare record ID used for subsequent cleanup
        /// deletion.</para>
        /// </remarks>
        [LoggerMessage(EventId = 239, Level = LogLevel.Debug,
            Message = "Certificates: Created Cloudflare TXT record {Id} for {Name}")]
        private partial void LogCreatedCloudflareTxtRecord(string id, string name);

        /// <summary>
        /// Logs that a DNS TXT record was successfully cleaned up.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="CleanupTxtRecordsAsync"/> -- after
        /// <see cref="DeleteCloudflareTxtRecordAsync"/> succeeds for an individual record.  Guarded by
        /// <c>logger.IsEnabled(LogLevel.Debug)</c>.</para>
        /// </remarks>
        [LoggerMessage(EventId = 240, Level = LogLevel.Debug,
            Message = "Certificates: Cleaned up DNS TXT record: {Name}")]
        private partial void LogCleanedUpTxtRecord(string name);

        /// <summary>
        /// Logs that a DNS TXT record cleanup deletion failed.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="CleanupTxtRecordsAsync"/> -- in the per-record <c>catch</c> block when
        /// <see cref="DeleteCloudflareTxtRecordAsync"/> throws.  The original exception is passed for diagnostics.
        /// Both the record name and Cloudflare record ID are logged to enable manual cleanup via the Cloudflare
        /// dashboard or API if needed.</para>
        /// </remarks>
        [LoggerMessage(EventId = 241, Level = LogLevel.Warning,
            Message = "Certificates: Failed to delete DNS TXT record {Name} ({Id})")]
        private partial void LogTxtRecordCleanupFailed(Exception ex, string name, string id);

        #endregion

        #region Logging Methods -- RequestCertificateAsync (250-259)

        /// <summary>
        /// Logs the start of an ACME certificate request, including the domain count and directory (staging/production).
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="RequestCertificateAsync"/> -- first statement in the method, before any ACME
        /// or Cloudflare API calls.  The <c>{Directory}</c> parameter is the string <c>"staging"</c> or
        /// <c>"production"</c> (interned literals -- no allocation).</para>
        /// </remarks>
        [LoggerMessage(EventId = 250, Level = LogLevel.Information,
            Message = "Certificates: Starting ACME certificate request for {DomainCount} domain(s) via {Directory}")]
        private partial void LogStartingCertificateRequest(int domainCount, string directory);

        #endregion
    }

}
