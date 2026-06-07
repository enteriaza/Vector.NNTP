// <copyright file="CertificateStore.Persistence.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateStore.Persistence.cs — Save/load operations for PFX certificates, ACME account keys, and certificate
// private keys.
//
// SaveCertificateAsync           — Atomically persists a PFX certificate to certs/certificate.pfx.
// TryLoadCachedCertificate       — Loads a cached PFX from disk, disposes if expired, returns null on absence or error.
// TryLoadCertificateBytesAsync   — Reads raw PFX bytes for cluster broadcast payloads.
// SaveAccountKeyAsync            — Atomically persists the ACME account key PEM to certs/letsencrypt.pem.
// TryLoadAccountKeyAsync         — Reads the ACME account key PEM, returns null on absence or error.
// SaveCertificateKeyAsync        — Atomically persists the certificate private key PEM to certs/certificate-key.pem.
// TryLoadCertificateKeyAsync     — Reads the certificate private key PEM, returns null on absence or error.
//
// All save methods delegate to IOUtilities.AtomicWriteAsync for temp-file + fsync + rename durability.
// All load methods delegate to IOUtilities.TryReadFileAsync for TOCTOU-safe, cancellation-aware error handling.
//
// Exception hierarchy (TryLoadCachedCertificate):
//   OperationCanceledException                        — rethrown (host shutdown must propagate)
//   FileNotFoundException / DirectoryNotFoundException — silent null (first-run, no cached file)
//   CryptographicException                            — LogCachedCertificateCorrupt, null (permanent, needs renewal)
//   Exception                                         — LogCachedCertificateLoadFailed, null (may be transient I/O)
//
// Cross-platform:
//   All methods use BCL APIs (File.ReadAllBytesAsync, File.ReadAllTextAsync, X509Certificate2 constructor) that are
//   available on both Windows (x64) and Linux (x64).  Platform-specific file permissions are handled by the
//   IOUtilities layer.  No P/Invoke, no architecture-specific intrinsics.
//
// SIMD applicability:
//   Not applicable.  All operations are I/O-bound filesystem reads/writes or single-certificate parsing.  There are
//   no contiguous memory buffers or vectorisable computation paths.
//
// Callers:
//   CertificateRenewalService  — TryLoadCachedCertificate on startup.
//   AcmeCertificateProvider    — account key, certificate key, and PFX persistence during ACME renewal.
//   CertificateClusterSync     — TryLoadCertificateBytesAsync for broadcast, SaveCertificateAsync for peer adoption.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Encryption.Certificates
{
    /// <summary>
    /// Persistence partial for <see cref="CertificateStore"/> PFX, account-key, and certificate-key save/load paths.
    /// </summary>
    /// <remarks>
    /// All writes use atomic replace; loads use resilient read helpers with documented exception-to-null mapping.
    /// </remarks>
    internal sealed partial class CertificateStore
    {
        #region Internal Methods — Certificate

        /// <summary>
        /// Attempts to load a cached certificate from disk.  Returns <see langword="null"/> if the file does not exist, is
        /// expired, or cannot be parsed.
        /// </summary>
        /// <remarks>
        /// <para><b>Cancellation:</b> <see cref="OperationCanceledException"/> is caught and rethrown to ensure host
        /// shutdown propagates cleanly.  Without this explicit rethrow, the general <c>catch (Exception)</c> handler would
        /// swallow shutdown cancellation and log it as a warning — masking the legitimate shutdown signal.</para>
        ///
        /// <para><b>Expiry check:</b> An expired certificate is disposed via <see cref="DisposeCertificate"/> (which
        /// cleans up the persisted CNG key on Windows) and <see langword="null"/> is returned — the caller proceeds to
        /// ACME renewal.</para>
        ///
        /// <para><b>TOCTOU safe:</b> The file is opened directly without a preceding <c>File.Exists()</c> check.
        /// <see cref="FileNotFoundException"/> is caught separately to suppress the log message for first-run scenarios
        /// (no cached certificate on disk is expected, not an error).</para>
        ///
        /// <para><b>Directory absence:</b> <see cref="DirectoryNotFoundException"/> is caught and treated identically to
        /// <see cref="FileNotFoundException"/>.  On first run, <see cref="EnsureCertsDirectory"/> may not yet have been
        /// called when this method is invoked.  The <see cref="X509Certificate2(string, string?, X509KeyStorageFlags)"/>
        /// constructor uses <c>File.Open</c> internally, which throws <see cref="DirectoryNotFoundException"/> — not
        /// <see cref="FileNotFoundException"/> — when the parent directory is missing.</para>
        ///
        /// <para><b>Corrupt PFX detection:</b> <see cref="CryptographicException"/> is caught separately from general
        /// <see cref="Exception"/> to provide a distinct log message (<see cref="LogCachedCertificateCorrupt"/>).  A
        /// corrupt PFX will never self-heal and requires a new ACME renewal, while I/O errors may be transient.</para>
        ///
        /// <para><b>Synchronous:</b> This method is synchronous because the <see cref="X509Certificate2(string, string?,
        /// X509KeyStorageFlags)"/> constructor performs synchronous file I/O — there is no async overload for PFX loading
        /// from a file path in .NET 8.  Called once at startup, so synchronous I/O does not block any hot path.</para>
        /// </remarks>
        /// <returns>The loaded certificate if valid, or <see langword="null"/> if absent, expired, corrupt, or
        /// unreadable.</returns>
        /// <exception cref="OperationCanceledException">The host is shutting down.</exception>
        internal X509Certificate2? TryLoadCachedCertificate()
        {
            try
            {
                X509Certificate2 cert = new(CertificatePath, _pfxExportPassword, CertificateDefaults.PfxKeyStorageFlags);

                if (cert.NotAfter > DateTime.UtcNow)
                {
                    LogLoadedCachedCertificate(cert.Subject, cert.Thumbprint, cert.NotAfter);
                    return cert;
                }

                LogCachedCertificateExpired(cert.NotAfter);
                DisposeCertificate(cert);
                return null;
            }
            catch (OperationCanceledException)
            {
                // Host shutdown — rethrow so the caller's cancellation filter handles it cleanly.  Without this
                // explicit handler, the general catch (Exception) below would swallow the cancellation and log it
                // as a warning, masking the legitimate shutdown signal.
                throw;
            }
            catch (FileNotFoundException)
            {
                // No cached certificate on disk — expected on first run, not an error.
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                // Parent directory (certs/) does not exist yet — same semantic as file-not-found.
                return null;
            }
            catch (CryptographicException ex)
            {
                // The file exists but contains invalid PKCS#12 data — truncated write, filesystem corruption, or manual
                // editing.  A corrupt PFX will never self-heal; the caller proceeds to ACME renewal.
                LogCachedCertificateCorrupt(ex, CertificatePath);
                return null;
            }
            catch (Exception ex)
            {
                // Permission denied, disk I/O failure, or other unexpected error.  The caller proceeds to ACME renewal.
                LogCachedCertificateLoadFailed(ex, CertificatePath);
                return null;
            }
        }

        /// <summary>
        /// Atomically persists a PKCS#12 (PFX) certificate to <see cref="CertificatePath"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Atomicity and durability:</b> Delegates to <see cref="FileIOUtilities.AtomicWriteAsync"/> which writes
        /// to a temp file, fsync's to stable storage, atomically renames to the target path, and sets <c>0600</c>
        /// permissions on Linux.</para>
        ///
        /// <para><b>Callers:</b></para>
        /// <list type="bullet">
        ///   <item><description><c>AcmeCertificateProvider.FinaliseOrderAsync</c> — persists the newly-issued PFX
        ///     after ACME order download.</description></item>
        ///   <item><description><c>CertificateClusterSync</c>.TryAdoptPeerCertificateAsync"/> — persists a peer's PFX
        ///     received via RabbitMQ broadcast.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="pfxBytes">The PFX bytes to write (no password protection).</param>
        /// <param name="ct">Cancellation token.  If cancelled during the write, the temp file is cleaned up and the
        /// existing certificate file (if any) remains untouched.</param>
        /// <exception cref="IOException">The temp file could not be written, flushed, or renamed.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">The process lacks write permission.</exception>
        internal async Task SaveCertificateAsync(byte[] pfxBytes, CancellationToken ct)
        {
            await FileIOUtilities.AtomicWriteAsync(CertificatePath, pfxBytes, ct).ConfigureAwait(false);
            LogCertificateSaved(CertificatePath);
        }

        /// <summary>
        /// Reads the raw PFX bytes from <see cref="CertificatePath"/>.  Returns <see langword="null"/> if the file does
        /// not exist or cannot be read.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <c>CertificateClusterSync</c>.PublishCertificateStateAsync"/> — builds the broadcast
        /// payload.  Returns <see langword="null"/> on any I/O error so the caller logs a warning and skips the broadcast
        /// rather than crashing.</para>
        ///
        /// <para><b>Error handling:</b> Delegates to <see cref="FileIOUtilities.TryReadFileAsync{T}"/> which suppresses
        /// <see cref="FileNotFoundException"/> and <see cref="DirectoryNotFoundException"/> (first-run), rethrows
        /// <see cref="OperationCanceledException"/> (host shutdown), and invokes the error callback for all other
        /// errors.</para>
        /// </remarks>
        /// <param name="ct">Cancellation token.  If cancelled, <see cref="OperationCanceledException"/> propagates.</param>
        /// <returns>The PFX bytes, or <see langword="null"/> if the file is absent or unreadable.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
        internal async Task<byte[]?> TryLoadCertificateBytesAsync(CancellationToken ct)
        {
            return await FileIOUtilities.TryReadFileAsync(
                        File.ReadAllBytesAsync, CertificatePath,
                        ex => LogFileReadFailed(ex, "certificate bytes", CertificatePath), ct).ConfigureAwait(false);
        }

        #endregion

        #region Internal Methods — ACME Account Key

        /// <summary>
        /// Atomically persists the ACME account private key PEM to <see cref="AccountKeyPath"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Atomicity and durability:</b> Delegates to <see cref="FileIOUtilities.AtomicWriteAsync"/> which writes
        /// to a temp file, fsync's to stable storage, atomically renames to the target path, and sets <c>0600</c>
        /// permissions on Linux.</para>
        ///
        /// <para><b>Callers:</b></para>
        /// <list type="bullet">
        ///   <item><description><c>AcmeCertificateProvider.LoadOrCreateAccountAsync</c> — persists the account key
        ///     after creating a new ACME account.</description></item>
        ///   <item><description><c>CertificateClusterSync</c>.TryAdoptPeerCertificateAsync"/> — persists the leader's
        ///     account key received via broadcast.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="pem">The PEM-encoded account key.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="IOException">The temp file could not be written, flushed, or renamed.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">The process lacks write permission.</exception>
        internal async Task SaveAccountKeyAsync(string pem, CancellationToken ct)
        {
            await FileIOUtilities.AtomicWriteAsync(AccountKeyPath, Encoding.UTF8.GetBytes(pem), ct).ConfigureAwait(false);
            LogAccountKeySaved(AccountKeyPath);
        }

        /// <summary>
        /// Reads the ACME account private key PEM from <see cref="AccountKeyPath"/>.  Returns <see langword="null"/> if
        /// the file does not exist or cannot be read.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <c>AcmeCertificateProvider.LoadOrCreateAccountAsync</c> — loads the existing
        /// account key to reuse the ACME account.  If <see langword="null"/> is returned, the caller creates a new ACME
        /// account and persists the freshly generated key via <see cref="SaveAccountKeyAsync"/>.</para>
        ///
        /// <para><b>Error handling:</b> Delegates to <see cref="FileIOUtilities.TryReadFileAsync{T}"/> which suppresses
        /// <see cref="FileNotFoundException"/> and <see cref="DirectoryNotFoundException"/> (first-run), rethrows
        /// <see cref="OperationCanceledException"/> (host shutdown), and invokes the error callback for all other
        /// errors.</para>
        /// </remarks>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The PEM string, or <see langword="null"/> if the file is absent or unreadable.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
        internal async Task<string?> TryLoadAccountKeyAsync(CancellationToken ct)
        {
            return await FileIOUtilities.TryReadFileAsync(
                        File.ReadAllTextAsync, AccountKeyPath,
                        ex => LogFileReadFailed(ex, "ACME account key", AccountKeyPath), ct).ConfigureAwait(false);
        }

        #endregion

        #region Internal Methods — Certificate Key

        /// <summary>
        /// Atomically persists the certificate private key PEM to <see cref="CertificateKeyPath"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Atomicity and durability:</b> Delegates to <see cref="FileIOUtilities.AtomicWriteAsync"/> which writes
        /// to a temp file, fsync's to stable storage, atomically renames to the target path, and sets <c>0600</c>
        /// permissions on Linux.</para>
        ///
        /// <para><b>Callers:</b></para>
        /// <list type="bullet">
        ///   <item><description><c>AcmeCertificateProvider.LoadOrCreateCertificateKeyAsync</c> — persists the
        ///     certificate key after generating a new ES256 key.</description></item>
        ///   <item><description><c>CertificateClusterSync</c>.TryAdoptPeerCertificateAsync"/> — persists the leader's
        ///     certificate key received via broadcast.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="pem">The PEM-encoded certificate key (ES256).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="IOException">The temp file could not be written, flushed, or renamed.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">The process lacks write permission.</exception>
        internal async Task SaveCertificateKeyAsync(string pem, CancellationToken ct)
        {
            await FileIOUtilities.AtomicWriteAsync(CertificateKeyPath, Encoding.UTF8.GetBytes(pem), ct).ConfigureAwait(false);
            LogCertificateKeySaved(CertificateKeyPath);
        }

        /// <summary>
        /// Reads the certificate private key PEM from <see cref="CertificateKeyPath"/>.  Returns <see langword="null"/> if
        /// the file does not exist or cannot be read.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <c>AcmeCertificateProvider.LoadOrCreateCertificateKeyAsync</c> — loads the existing
        /// certificate key to reuse across renewals.  If <see langword="null"/> is returned, the caller generates a new
        /// ES256 key and persists it via <see cref="SaveCertificateKeyAsync"/>.</para>
        ///
        /// <para><b>Error handling:</b> Delegates to <see cref="FileIOUtilities.TryReadFileAsync{T}"/> which suppresses
        /// <see cref="FileNotFoundException"/> and <see cref="DirectoryNotFoundException"/> (first-run), rethrows
        /// <see cref="OperationCanceledException"/> (host shutdown), and invokes the error callback for all other
        /// errors.</para>
        /// </remarks>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The PEM string, or <see langword="null"/> if the file is absent or unreadable.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
        internal async Task<string?> TryLoadCertificateKeyAsync(CancellationToken ct)
        {
            return await FileIOUtilities.TryReadFileAsync(
                        File.ReadAllTextAsync, CertificateKeyPath,
                        ex => LogFileReadFailed(ex, "certificate key", CertificateKeyPath), ct).ConfigureAwait(false);
        }

        #endregion
    }

}
