// <copyright file="AcmeCertificateProvider.OrderFinalisation.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AcmeCertificateProvider.OrderFinalisation.cs — Order readiness polling, CSR generation, PFX construction from the
// certificate chain, and certificate key management.
//
// After all DNS-01 challenges are validated, this partial handles the remaining ACME order lifecycle: waiting for the
// order to reach 'ready' status, generating and submitting the CSR, downloading the issued certificate chain, building
// a PFX archive that bypasses Certes' internal chain resolution (which fails for staging intermediates), and persisting
// the result to disk.
//
// Methods:
//   FinaliseOrderAsync              -- Orchestrates the entire post-challenge order lifecycle: readiness polling -> CSR
//                                      submission -> certificate download -> PFX construction -> disk persistence.
//   WaitForOrderStatusAsync         -- Unified polling loop for order status transitions (Ready, Valid).
//   LoadOrCreateCertificateKeyAsync -- Loads or generates the ES256 certificate private key with corrupt-file recovery.
//
// Refactored methods (moved to Utilities\CryptoUtilities.cs):
//   CryptoUtilities.ImportEcdsaPrivateKey -- Imports a Certes IKey into a .NET ECDsa instance with exception-safe disposal.
//   CryptoUtilities.BuildPfxFromChain     -- Constructs a PKCS#12 archive from raw DER bytes, bypassing Certes' chain resolution.
//   CryptoUtilities.CreateCsr             -- Builds a DER-encoded PKCS#10 CSR with Key Usage, EKU, and SAN extensions.
//
// Cross-platform:
//   Fully portable.  All methods use BCL APIs available on all .NET 8 runtimes (Windows x64, Linux x64).  No
//   P/Invoke, no OS-specific APIs.  PFX key storage flags are resolved by CertificateDefaults.PfxKeyStorageFlags
//   via OperatingSystem.IsWindows().  X500DistinguishedNameBuilder, CertificateRequest, and SubjectAlternativeNameBuilder
//   are all BCL types available on all supported platforms.
//
// SIMD applicability:
//   Not applicable.  This file performs ACME order polling (HTTP), CSR generation (ASN.1/DER), PFX construction
//   (PKCS#12), and key import (PKCS#8).  There are no contiguous memory buffers, byte-level pattern searches, or
//   bulk numeric operations that would benefit from vector instructions.
//
// Callers:
//   AcmeCertificateProvider.RequestCertificateAsync -- sole consumer via FinaliseOrderAsync

using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using System.Security.Cryptography.X509Certificates;

using Vector.NNTP.Encryption.Acme;
using Vector.NNTP.Utilities.Cryptography;

namespace Vector.NNTP.Encryption.Certificates.Acme
{

    internal sealed partial class AcmeCertificateProvider
    {
        #region Private Methods -- Order Finalisation

        /// <summary>
        /// Finalises the ACME order: waits for the order to reach <c>ready</c> status, loads or generates the certificate
        /// private key, generates the CSR, submits the finalize request, waits for the certificate to be issued, downloads
        /// the certificate chain, builds a PFX, and persists it to disk.
        /// </summary>
        /// <remarks>
        /// <para>The certificate private key (ES256) is persisted to <c>certs/certificate-key.pem</c> and reused across
        /// renewals.  This keeps the public key (and therefore certificate fingerprint) stable, avoids key-pinning issues
        /// if DANE TLSA records are in use, and slightly reduces CPU during renewal.</para>
        ///
        /// <para><b>Order readiness polling:</b> After all DNS-01 challenges are validated, the ACME server may not
        /// immediately transition the order to <see cref="OrderStatus.Ready"/>.  This method polls the order resource at
        /// <see cref="OrderPollInterval"/> intervals for up to <see cref="OrderPollMaxAttempts"/> attempts, waiting
        /// for the server to process all authorization completions before submitting the CSR.</para>
        ///
        /// <para><b>Why <c>order.Finalize()</c> + <c>order.Download()</c> instead of <c>order.Generate()</c>:</b> The
        /// Certes <c>IOrderContextExtensions.Generate()</c> convenience method internally re-fetches the order resource and
        /// performs its own status checks before calling <c>Finalize()</c>.  This creates a race condition: our
        /// <see cref="WaitForOrderStatusAsync"/> confirms <c>Ready</c>, but by the time <c>Generate()</c> re-fetches, the
        /// order may have transiently moved to <c>Processing</c> (from a prior finalize attempt on a retry cycle) or the
        /// ACME server may return a stale response -- causing <c>Generate()</c> to throw
        /// <see cref="Certes.AcmeException"/>(<c>"Fail to finalize order"</c>).  Calling <c>Finalize()</c> directly
        /// submits the CSR without redundant status checks, then <see cref="WaitForOrderStatusAsync"/> polls until the
        /// certificate is issued.</para>
        ///
        /// <para><b>Cancellation coverage:</b> The Certes <see cref="IOrderContext.Finalize"/> and
        /// <see cref="IOrderContext.Download"/> methods do not accept a <see cref="CancellationToken"/>.  Explicit
        /// <see cref="CancellationToken.ThrowIfCancellationRequested"/> calls are placed before each to ensure a host
        /// shutdown that occurs during the preceding <see langword="await"/> propagates promptly rather than initiating a
        /// new outbound ACME request.  If the ACME server hangs, these calls block indefinitely until the underlying
        /// <see cref="HttpClient.Timeout"/> fires (Certes defaults to 100 s).  The <paramref name="ct"/> is also checked
        /// between each step via the <see cref="Task.Delay(TimeSpan, CancellationToken)"/> in
        /// <see cref="WaitForOrderStatusAsync"/>.  A hung <c>Finalize</c>/<c>Download</c> will eventually time out and
        /// surface as an exception in the outer retry loop.</para>
        /// </remarks>
        /// <param name="order">The ACME order to finalise.</param>
        /// <param name="orderDomains">DNS identifiers included in the CSR.</param>
        /// <param name="store">Filesystem persistence for the PFX and certificate key.</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>The newly provisioned <see cref="X509Certificate2"/> imported with
        /// <see cref="CertificateDefaults.PfxKeyStorageFlags"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the order transitions to
        /// <see cref="OrderStatus.Invalid"/> or does not reach <see cref="OrderStatus.Ready"/>/<see cref="OrderStatus.Valid"/>
        /// within the retry budget.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task<X509Certificate2> FinaliseOrderAsync(
            IOrderContext order,
            string[] orderDomains,
            CertificateStore store,
            CancellationToken ct)
        {
            // Wait for the order to reach 'ready' status.  After all challenges are validated, the ACME server may still
            // be processing authorization completions -- submitting a CSR while the order is 'pending' causes the ACME
            // server to reject the finalize request.
            await WaitForOrderStatusAsync(order, [OrderStatus.Ready, OrderStatus.Valid],
                "proceeding to finalization", "ACME order did not reach 'ready' status", ct).ConfigureAwait(false);

            IKey privateKey = await LoadOrCreateCertificateKeyAsync(store, ct).ConfigureAwait(false);

            // Build and submit the CSR directly via Finalize() rather than the Generate() convenience method.  Generate()
            // re-fetches the order status internally and throws "Fail to finalize order" if it sees a transient
            // non-Ready state -- a race condition when the server has already begun processing a prior finalize request.
            byte[] csrBytes = HashingUtilities.CreateCsr(orderDomains, privateKey);

            // Certes' Finalize() does not accept a CancellationToken -- check before the outbound ACME request.
            ct.ThrowIfCancellationRequested();
            Order finalized = await AcmeTransientRetry.ExecuteAsync(
                () => order.Finalize(csrBytes),
                logger,
                "Acme.Finalize",
                options.AcmeTransientRetryMaxAttempts,
                ct).ConfigureAwait(false);

            if (logger.IsEnabled(LogLevel.Debug))
                LogOrderFinalized(finalized.Status ?? default, order.Location);

            // Wait for the order to transition from 'processing' to 'valid' (certificate issued).
            await WaitForOrderStatusAsync(order, [OrderStatus.Valid],
                "certificate ready for download", "ACME order did not reach 'valid' status after finalization", ct).ConfigureAwait(false);

            // Certes' Download() does not accept a CancellationToken -- check before the outbound ACME request.
            ct.ThrowIfCancellationRequested();
            CertificateChain certChain = await AcmeTransientRetry.ExecuteAsync(
                () => order.Download(),
                logger,
                "Acme.Download",
                options.AcmeTransientRetryMaxAttempts,
                ct).ConfigureAwait(false);

            string? pfxPassword = options.PfxExportPassword;
            byte[] pfxBytes = HashingUtilities.BuildPfxFromChain(certChain, privateKey, pfxPassword);
            await store.SaveCertificateAsync(pfxBytes, ct).ConfigureAwait(false);

            return new X509Certificate2(pfxBytes, pfxPassword, CertificateDefaults.PfxKeyStorageFlags);
        }

        #endregion

        #region Private Methods -- Order Status Polling

        /// <summary>
        /// Polls the ACME order resource until its status transitions to one of the <paramref name="acceptedStatuses"/>,
        /// or raises an <see cref="InvalidOperationException"/> on terminal failure or timeout.
        /// </summary>
        /// <remarks>
        /// <para><b>Unified polling:</b> This method replaces the previous <c>WaitForOrderReadyAsync</c> and
        /// <c>WaitForOrderValidAsync</c> methods which had identical structure -- poll loop, terminal-state check, final
        /// check after the last poll, timeout error.  The only differences were the accepted status set, the success log
        /// message, and the timeout error message -- all now parameterised.</para>
        ///
        /// <para><b>Early exit:</b> If the order is already in one of the <paramref name="acceptedStatuses"/> (e.g.
        /// <see cref="OrderStatus.Valid"/> on a retry after a partial failure), the method returns immediately on the first
        /// poll.</para>
        ///
        /// <para><b>Terminal states:</b> <see cref="OrderStatus.Invalid"/> indicates the order has failed permanently.
        /// An <see cref="InvalidOperationException"/> is thrown so the caller does not attempt CSR submission or certificate
        /// download on a dead order.  The <see cref="Order.Error"/> property (an <see langword="object"/>?) is included in
        /// the exception message via its <see cref="object.ToString"/> representation for diagnostic context.</para>
        ///
        /// <para><b>Final check after last poll:</b> After the loop exhausts <see cref="OrderPollMaxAttempts"/>, one final
        /// <see cref="IOrderContext.Resource"/> fetch is performed.  This avoids a spurious timeout when the server
        /// transitions the order during the last <see cref="OrderPollInterval"/> delay.</para>
        ///
        /// <para><b>Nullable <see cref="OrderStatus"/>:</b> The Certes library models <c>Order.Status</c> as
        /// <see cref="OrderStatus"/>? (nullable) because the ACME JSON response may omit the <c>status</c> field.
        /// The <paramref name="acceptedStatuses"/> parameter uses non-nullable <see cref="OrderStatus"/> because
        /// callers never accept a <see langword="null"/> status as success.  The comparison uses
        /// <see cref="Array.IndexOf{T}(T[], T)"/> instead of <see cref="Enumerable.Contains{T}(IEnumerable{T}, T)"/>
        /// to avoid LINQ's <see cref="IEnumerator{T}"/> allocation on each poll iteration.</para>
        ///
        /// <para><b>Log string pre-computation:</b> The <c>string.Join(" or ", acceptedStatuses)</c> for the polling log
        /// message is computed once before the loop rather than on every iteration, eliminating up to
        /// <see cref="OrderPollMaxAttempts"/> redundant <see cref="string.Join{T}(string?, IEnumerable{T})"/>
        /// allocations.</para>
        ///
        /// <para><b>Cancellation coverage:</b> The Certes <see cref="IOrderContext.Resource"/> method does not accept a
        /// <see cref="CancellationToken"/>.  The <see cref="Task.Delay(TimeSpan, CancellationToken)"/> call at the end
        /// of each iteration provides the cancellation check point.  If <c>Resource()</c> hangs, the underlying
        /// <see cref="HttpClient.Timeout"/> (Certes default: 100 s) will eventually surface as an exception.</para>
        /// </remarks>
        /// <param name="order">The ACME order to poll.</param>
        /// <param name="acceptedStatuses">The set of non-nullable <see cref="OrderStatus"/> values that indicate
        /// success.  A <see langword="null"/> <c>resource.Status</c> is never matched.</param>
        /// <param name="successLogSuffix">Text appended to the debug log message on success (e.g.
        /// <c>"proceeding to finalization"</c>).</param>
        /// <param name="timeoutErrorPrefix">Text prepended to the timeout error message (e.g. <c>"ACME order did not reach
        /// 'ready' status"</c>).</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <exception cref="InvalidOperationException">Thrown when the order transitions to
        /// <see cref="OrderStatus.Invalid"/> or does not reach one of the <paramref name="acceptedStatuses"/> within the
        /// retry budget.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task WaitForOrderStatusAsync(
            IOrderContext order,
            OrderStatus[] acceptedStatuses,
            string successLogSuffix,
            string timeoutErrorPrefix,
            CancellationToken ct)
        {
            // Pre-compute the expected-status display string outside the loop to avoid allocating a new
            // string.Join result on every poll iteration (up to OrderPollMaxAttempts times).
            string? expectedStatusDisplay = null;

            for (int attempt = 1; attempt <= OrderPollMaxAttempts; attempt++)
            {
                Order resource = await order.Resource().ConfigureAwait(false);

                if (resource.Status.HasValue && Array.IndexOf(acceptedStatuses, resource.Status.Value) >= 0)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                        LogOrderStatusAccepted(resource.Status.Value, successLogSuffix);
                    return;
                }

                if (resource.Status == OrderStatus.Invalid)
                    throw new InvalidOperationException($"ACME order is invalid. Error: {resource.Error ?? "unknown"}");

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    expectedStatusDisplay ??= string.Join(" or ", acceptedStatuses);
                    LogOrderPollingStatus(resource.Status ?? default, expectedStatusDisplay, attempt, OrderPollMaxAttempts);
                }

                await Task.Delay(OrderPollInterval, ct).ConfigureAwait(false);
            }

            // Final check after the last poll -- avoids a spurious timeout when the server transitions the order
            // during the last OrderPollInterval delay.
            Order finalResource = await order.Resource().ConfigureAwait(false);

            if (finalResource.Status.HasValue && Array.IndexOf(acceptedStatuses, finalResource.Status.Value) >= 0)
                return;

            if (finalResource.Status == OrderStatus.Invalid)
                throw new InvalidOperationException($"ACME order is invalid. Error: {finalResource.Error ?? "unknown"}");

            throw new InvalidOperationException(
                $"{timeoutErrorPrefix} within {OrderPollMaxAttempts * OrderPollInterval.TotalSeconds:F0}s " +
                $"(final status: {finalResource.Status})");
        }

        #endregion

        #region Private Methods -- Certificate Key Management

        /// <summary>
        /// Loads the certificate private key from disk, or generates a new ES256 key and persists it.
        /// </summary>
        /// <remarks>
        /// <para><b>Key reuse:</b> Reusing the same key across renewals keeps the public key (and therefore the
        /// certificate fingerprint) stable.  This avoids key-pinning issues if DANE TLSA records are in use and slightly
        /// reduces CPU during renewal (no key generation).</para>
        ///
        /// <para><b>Corrupt key recovery:</b> If the PEM file is present but contains invalid data (truncated write
        /// surviving a power loss, filesystem corruption, manual editing error), <see cref="KeyFactory.FromPem"/> throws.
        /// Rather than propagating the exception (which would cause
        /// <see cref="CertificateRenewalService.ExecuteAsync"/>'s retry loop to re-read the same corrupt file on every
        /// attempt -- an infinite failure loop), the parse error is caught, logged at <see cref="LogLevel.Warning"/> with
        /// the exception, and the method falls through to generate a fresh key.
        /// <see cref="CertificateStore.SaveCertificateKeyAsync"/> overwrites the corrupt file atomically via temp-file +
        /// rename.</para>
        ///
        /// <para><b>Key change impact:</b> Generating a new key changes the certificate's public key, which means any
        /// existing DANE TLSA 3-1-1 records (matching the public key) will fail validation until updated.  This is an
        /// acceptable trade-off -- a corrupt key file means the previous key is irrecoverable, and a failed renewal (no TLS
        /// certificate at all) is worse than a TLSA mismatch.</para>
        /// </remarks>
        /// <param name="store">Filesystem persistence for the key PEM.</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>The certificate private key.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task<IKey> LoadOrCreateCertificateKeyAsync(CertificateStore store, CancellationToken ct)
        {
            string? existingPem = await store.TryLoadCertificateKeyAsync(ct).ConfigureAwait(false);

            if (existingPem is not null)
            {
                try
                {
                    IKey key = KeyFactory.FromPem(existingPem);

                    if (logger.IsEnabled(LogLevel.Debug))
                        LogLoadedExistingCertificateKey(store.CertificateKeyPath);
                    return key;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The PEM file was readable but contains invalid data -- truncated write, filesystem corruption,
                    // or manual editing error.  Fall through to generate a new key, which will atomically overwrite
                    // the corrupt file.  Without this catch, the retry loop would re-read the same corrupt file on
                    // every attempt, creating an infinite failure cycle.
                    LogCertificateKeyCorrupt(ex, store.CertificateKeyPath);
                }
            }

            IKey newKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
            await store.SaveCertificateKeyAsync(newKey.ToPem(), ct).ConfigureAwait(false);

            LogGeneratedNewCertificateKey(store.CertificateKeyPath);
            return newKey;
        }

        #endregion
    }

}
