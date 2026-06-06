// <copyright file="AcmeCertificateProvider.AcmeAccount.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AcmeCertificateProvider.AcmeAccount.cs — ACME account key loading from configuration and context creation.
//
// The account key is loaded from LetsEncryptOptions.AccountKeyPem (bound from appsettings.json) rather than from
// disk.  This ensures all cluster nodes use the same ACME account deterministically -- a fresh node that cannot
// retrieve the account key from RabbitMQ will not silently register a new account and invalidate certificates on
// other running hosts.
//
// The key is validated at startup by LetsEncryptOptions.NormaliseAndValidateAccountKeyPem (PEM header check +
// KeyFactory.FromPem parse), so LoadOrCreateAccountAsync can assume the PEM is structurally valid.  If the ACME
// server rejects the key (revoked, unknown account), the exception propagates to the caller's retry loop in
// CertificateRenewalService.ExecuteAsync.
//
// The disk-based account key file (certs/letsencrypt.pem) is still written after a successful ACME context
// creation -- this keeps the on-disk copy in sync for cluster broadcast payloads.  However, the authoritative
// source is always the configuration value.
//
// Methods:
//   LoadOrCreateAccountAsync -- Creates an AcmeContext from the configuration-provided account key and verifies
//                               or registers the account with the ACME server.
//
// Security:
//   The PEM key content is never logged, interpolated into exception messages, or exposed via structured logging
//   properties.  The only logged values are the key source descriptor ("appsettings.json (AccountKeyPem)") and
//   the operator-configured contact email (options.AcmeAccountEmail).
//
// Cross-platform:
//   Fully portable.  All methods use BCL APIs and the Certes library, both available on all .NET 8 runtimes
//   (Windows x64, Linux x64).  No P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.
//   Disk persistence is delegated to CertificateStore.SaveAccountKeyAsync which uses FileIOUtilities.AtomicWriteAsync
//   (temp file + fsync + rename, with 0600 permissions on Linux).
//
// SIMD applicability:
//   Not applicable.  This file performs ACME HTTP API calls, PEM key parsing, and a single disk write.  There
//   are no contiguous memory buffers, byte-level pattern searches, or bulk numeric operations that would benefit
//   from vector instructions.
//
// Callers:
//   AcmeCertificateProvider.RequestCertificateAsync -- sole consumer, called once per ACME renewal cycle.

using Certes;
using Certes.Acme;
using Vector.NNTP.Encryption.Acme;
using Vector.NNTP.Encryption.Configuration;

namespace Vector.NNTP.Encryption.Certificates.Acme
{

    internal sealed partial class AcmeCertificateProvider
    {
        #region Private Methods -- Clock Skew Guard

        /// <summary>
        /// Validates local clock skew against the ACME directory when the TTL cache misses.
        /// </summary>
        /// <param name="directoryUri">ACME directory URI for this issuance.</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>A task that completes when skew is acceptable or cached.</returns>
        /// <exception cref="InvalidOperationException">Thrown when skew exceeds configured limits.</exception>
        private async Task AssertClockSkewIfNeededAsync(Uri directoryUri, CancellationToken ct)
        {
            TimeSpan ttl = TimeSpan.FromMinutes(options.ClockSkewCheckTtlMinutes);
            if (ClockSkewTtlCache.TryHit(directoryUri, ttl))
                return;

            TimeSpan maxSkew = TimeSpan.FromMinutes(options.ClockSkewMaxMinutes);
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    await ClockSkewGuard.AssertSkewAcceptableAsync(logger, AcmeDirectoryHttpClient, directoryUri, maxSkew, ct)
                        .ConfigureAwait(false);
                    ClockSkewTtlCache.RecordSuccess(directoryUri);
                    return;
                }
                catch (InvalidOperationException ex) when (attempt < 2)
                {
                    LogClockSkewCheckRetry(ex);
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private Methods -- ACME Account Management

        /// <summary>
        /// Creates an <see cref="AcmeContext"/> from the account key configured in
        /// <see cref="LetsEncryptOptions.AccountKeyPem"/> and verifies the account exists with the ACME server.
        /// </summary>
        /// <remarks>
        /// <para>The account key is always loaded from configuration (<c>appsettings.json</c> or environment variable
        /// <c>LetsEncrypt__AccountKeyPem</c>) -- never from disk and never generated on the fly.  This eliminates the
        /// risk of a fresh node registering a new ACME account when RabbitMQ is unavailable, which would invalidate
        /// certificates issued under the previous account and break TLS across the cluster.</para>
        ///
        /// <para><b>Startup validation:</b> <see cref="LetsEncryptOptions.NormaliseAndValidateAccountKeyPem"/> already
        /// verified the PEM is structurally valid via <see cref="KeyFactory.FromPem"/> during options validation.  The
        /// <see cref="KeyFactory.FromPem"/> call here is therefore expected to succeed -- but if it fails due to a
        /// configuration reload, the exception propagates to the caller's retry loop.</para>
        ///
        /// <para><b>ACME server verification:</b> <see cref="AcmeContext.Account"/> is called to verify the account
        /// exists on the Let's Encrypt server.  If the account was never registered (first-time use of this key), a new
        /// account is registered with <see cref="AcmeContext.NewAccount"/>.  The key itself is never regenerated -- a
        /// rejected key (revoked, corrupted in transit) is a fatal configuration error that must be fixed by the
        /// operator.</para>
        ///
        /// <para><b>Disk persistence:</b> After a successful account context creation, the PEM is saved to disk via
        /// <see cref="CertificateStore.SaveAccountKeyAsync"/>.  This keeps the on-disk copy in sync for
        /// <c>CertificateClusterSync</c>.PublishCertificateStateAsync"/> which reads the account key from disk when
        /// building the broadcast payload.  This is a write-through cache -- the authoritative source remains the
        /// configuration value.</para>
        ///
        /// <para><b>Cancellation coverage:</b> The Certes <see cref="AcmeContext.Account"/> and
        /// <c>AcmeContext.NewAccount</c> methods do not accept a <see cref="CancellationToken"/>.
        /// A <see cref="CancellationToken.ThrowIfCancellationRequested"/> call before each ensures a host shutdown
        /// that occurs during the preceding <see langword="await"/> propagates promptly rather than initiating a
        /// new outbound ACME request.  If Certes' underlying <see cref="HttpClient"/> hangs, the default 100-second
        /// <see cref="HttpClient.Timeout"/> will surface as an exception in the caller's retry loop.</para>
        ///
        /// <para><b>Security:</b> The PEM key content is never logged, interpolated into exception messages, or
        /// exposed via structured logging properties.  The <c>source</c> parameter logged by
        /// <see cref="LogLoadedExistingAcmeAccount"/> and <see cref="LogCreatedNewAcmeAccount"/> is a fixed
        /// descriptor string (<c>"appsettings.json (AccountKeyPem)"</c>), not key material.  The <c>email</c>
        /// parameter is the operator-configured contact email from <see cref="LetsEncryptOptions.AcmeAccountEmail"/>, which is
        /// expected in diagnostic logs.</para>
        ///
        /// <para><b>Exception propagation order:</b> Exceptions propagate to the caller's retry loop in
        /// <see cref="CertificateRenewalService.ExecuteAsync"/> with implicit priority:</para>
        /// <list type="number">
        ///   <item><description><see cref="OperationCanceledException"/> -- host shutdown (from
        ///     <see cref="CancellationToken.ThrowIfCancellationRequested"/> guards or
        ///     <see cref="CertificateStore.SaveAccountKeyAsync"/>).</description></item>
        ///   <item><description><see cref="AcmeRequestException"/> -- Let's Encrypt rejected the account key or the
        ///     new account creation (from <see cref="AcmeContext.Account"/> or
        ///     <c>AcmeContext.NewAccount</c>).</description></item>
        ///   <item><description><see cref="InvalidOperationException"/> -- <see cref="LetsEncryptOptions.AccountKeyPem"/>
        ///     is empty (defensive guard -- startup validation prevents this).</description></item>
        ///   <item><description><see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> -- disk
        ///     persistence failure from <see cref="CertificateStore.SaveAccountKeyAsync"/> (atomic write, fsync, or
        ///     rename failed).</description></item>
        /// </list>
        /// </remarks>
        /// <param name="store">Filesystem persistence for the account key PEM (written for cluster broadcast
        /// compatibility).</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>An <see cref="AcmeContext"/> bound to the account.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        /// <exception cref="AcmeRequestException">Thrown when Let's Encrypt rejects the account key or the new account
        /// creation.  Propagates to the caller's retry loop.</exception>
        /// <exception cref="InvalidOperationException">Thrown when <see cref="LetsEncryptOptions.AccountKeyPem"/> is
        /// empty -- should not occur because startup validation enforces the required constraint, but guarded
        /// defensively.</exception>
        /// <exception cref="IOException">Thrown when the disk persistence of the account key PEM fails (atomic write,
        /// fsync, or rename).  Propagates from <see cref="CertificateStore.SaveAccountKeyAsync"/>.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the process lacks write permission to the
        /// certificate directory.  Propagates from <see cref="CertificateStore.SaveAccountKeyAsync"/>.</exception>
        private async Task<AcmeContext> LoadOrCreateAccountAsync(CertificateStore store, CancellationToken ct)
        {
            Uri directoryUri = options.UseStagingDirectory
                ? WellKnownServers.LetsEncryptStagingV2
                : WellKnownServers.LetsEncryptV2;

            if (logger.IsEnabled(LogLevel.Debug))
                LogUsingAcmeDirectory(directoryUri);

            // The account key is always sourced from configuration -- never from disk and never generated on the fly.
            // LetsEncryptOptions.NormaliseAndValidateAccountKeyPem already verified the PEM is structurally valid at
            // startup, so KeyFactory.FromPem is expected to succeed here.
            if (string.IsNullOrWhiteSpace(options.AccountKeyPem))
            {
                throw new InvalidOperationException(
                    "AccountKeyPem is not configured. The ACME account key must be provided in the LetsEncrypt " +
                    "configuration section. Without it, certificate renewal cannot proceed.");
            }

            IKey accountKey = KeyFactory.FromPem(options.AccountKeyPem);
            AcmeContext acme = new(directoryUri, accountKey);

            // Verify the account exists on the ACME server.  If this key was never registered (first-time use), fall
            // through to NewAccount below.  If the key is known, Account() succeeds and we return immediately.
            ct.ThrowIfCancellationRequested();
            try
            {
                _ = await acme.Account().ConfigureAwait(false);

                if (logger.IsEnabled(LogLevel.Debug))
                    LogLoadedExistingAcmeAccount("appsettings.json (AccountKeyPem)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The account does not exist on this ACME server (e.g. first use of this key, or staging vs production
                // mismatch).  Register a new account with the configured key -- the key itself is preserved, only the
                // server-side account record is created.
                LogAccountKeyNotRegistered(ex);

                ct.ThrowIfCancellationRequested();
                _ = await acme.NewAccount(options.AcmeAccountEmail, termsOfServiceAgreed: true).ConfigureAwait(false);

                LogCreatedNewAcmeAccount(options.AcmeAccountEmail, "appsettings.json (AccountKeyPem)");
            }

            // Persist the account key to disk so CertificateClusterSync.PublishCertificateStateAsync can include it in
            // the broadcast payload.  This is a write-through cache -- the authoritative source remains the configuration.
            await store.SaveAccountKeyAsync(acme.AccountKey.ToPem(), ct).ConfigureAwait(false);

            return acme;
        }

        #endregion
    }

}
