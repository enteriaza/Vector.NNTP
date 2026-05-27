// <copyright file="CertificateStore.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateStore.cs — Constants, fields, properties, directory management, certificate disposal, and deferred
// disposal scheduling.  All write operations are atomic (temp file + rename) to prevent corrupt files from partial
// writes during crashes or power loss.
//
// Partial files:
//   CertificateStore.cs             (this file) — Constants, fields, properties, directory management, certificate
//                                                 disposal, and deferred disposal scheduling.
//   CertificateStore.Persistence.cs — Save/load operations for PFX certificates, ACME account keys, and certificate
//                                     keys.  Each method delegates to IOUtilities.AtomicWriteAsync or
//                                     IOUtilities.TryReadFileAsync.
//   CertificateStore.Logging.cs     — [LoggerMessage] source-generated partial methods for all structured log
//                                     messages across all partial files.
//
// Files managed (all under the configured certs directory):
//   certificate.pfx      — cached TLS certificate (PKCS#12, no password)
//   letsencrypt.pem       — ACME account private key (PEM-encoded)
//   certificate-key.pem   — certificate private key (PEM-encoded, ES256)
//
// Security:
//   On Linux, all written files are set to 0600 (owner read/write only) via File.SetUnixFileMode after the atomic
//   rename.  The temp file is also set to 0600 BEFORE writing private key material — eliminating the permission race
//   window.  The certs/ directory is set to 0700 (owner only).  This prevents other users on the system from reading
//   private key material.  On Windows, NTFS ACLs are inherited from the parent directory.
//
//   No method in this class logs private key material, PFX bytes, or ACME account key content.  The Logging partial
//   logs only file paths (well-known certs/ locations), certificate subjects (public CN, visible in TLS handshakes),
//   thumbprints (SHA-1 of the public certificate), and expiry timestamps.
//
// Durability:
//   Written files are flushed to disk (fsync) before the atomic rename.  After the rename, the parent directory is
//   also fsync'd on Linux to ensure the directory entry update survives a power loss.  Without the file fsync, content
//   may only reach the OS page cache.  Without the directory fsync, the rename(2) directory entry may be lost on ext4
//   with data=writeback.  On Windows, FlushFileBuffers and NTFS MFT journaling provide equivalent guarantees.
//
// Callers:
//   CertificateRenewalService  — TryLoadCachedCertificate on startup, DisposeCertificate for CNG key cleanup,
//                                 DeferDisposal for superseded certificates.
//   AcmeCertificateProvider    — account key, certificate key, PFX persistence.
//   NntpListener               — DeferDisposal for hot-swapped TLS certificates.
//
// Cross-platform:
//   All methods run correctly on both Windows (x64) and Linux (x64).  Platform-specific behaviour (CNG key cleanup,
//   Unix file permissions) is guarded by OperatingSystem.IsWindows() and OperatingSystem.IsLinux() respectively.
//   No P/Invoke is used — all filesystem and cryptographic operations use BCL APIs.
//
// SIMD applicability:
//   Not applicable.  All operations are I/O-bound scalar filesystem calls or single-certificate cryptographic
//   operations.  There are no contiguous memory buffers, batch operations, or vectorisable computation paths.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vector.NNTP.Utilities.IO;
using Vector.NNTP.Encryption.Configuration;

namespace Vector.NNTP.Encryption.Certificates
{

    /// <summary>
    /// Manages certificate and ACME key persistence on the local filesystem.  All write operations are atomic (temp file +
    /// rename) to prevent corrupt files from partial writes during crashes or power loss.
    /// </summary>
    /// <remarks>
    /// <para><b>Files managed:</b></para>
    /// <list type="bullet">
    ///   <item><c>certificate.pfx</c> — cached TLS certificate (PKCS#12, no password protection).</item>
    ///   <item><c>letsencrypt.pem</c> — ACME account private key (PEM-encoded).</item>
    ///   <item><c>certificate-key.pem</c> — certificate private key (PEM-encoded, ES256).</item>
    /// </list>
    ///
    /// <para><b>Directory source:</b> The certificate directory path is provided by the caller (typically
    /// <see cref="LetsEncryptOptions.CertDir"/>) rather than hardcoded, allowing the directory to be configured via
    /// <c>LetsEncrypt:CertDir</c> in <c>appsettings.json</c>.</para>
    ///
    /// <para><b>Primary constructor parameters:</b></para>
    /// <list type="bullet">
    ///   <item><description><c>logger</c> — <see cref="ILogger"/> scoped to <see cref="CertificateRenewalService"/> for
    ///     consistent log context across the certificate subsystem.</description></item>
    ///   <item><description><c>certsDirectoryPath</c> — Resolved absolute path to the certificate directory, typically
    ///     from <see cref="LetsEncryptOptions.CertDir"/>.</description></item>
    /// </list>
    /// </remarks>
    internal sealed partial class CertificateStore(ILogger logger, string certsDirectoryPath, string? pfxExportPassword = null)
    {
        #region Constants and Fields

        /// <summary>
        /// Optional PFX password used when loading cached certificates from disk.
        /// </summary>
        private readonly string? _pfxExportPassword = pfxExportPassword;

        /// <summary>
        /// Absolute path to the certificate directory, provided by the caller.
        /// </summary>
        private readonly string _certsDirectoryPath = certsDirectoryPath;

        /// <summary>
        /// Logger instance captured from the primary constructor for use by <c>[LoggerMessage]</c> source-generated
        /// partial methods.
        /// </summary>
        private readonly ILogger _logger = logger;

        #endregion

        #region Properties

        /// <summary>Absolute path to the ACME account private key file (<c>letsencrypt.pem</c>).</summary>
        internal string AccountKeyPath { get; } = Path.Combine(certsDirectoryPath, LetsEncryptOptions.AccountKeyFileName);

        /// <summary>Absolute path to the cached TLS certificate file (<c>certificate.pfx</c>).</summary>
        internal string CertificatePath { get; } = Path.Combine(certsDirectoryPath, LetsEncryptOptions.CertificateFileName);

        /// <summary>Absolute path to the certificate private key file (<c>certificate-key.pem</c>).</summary>
        internal string CertificateKeyPath { get; } = Path.Combine(certsDirectoryPath, LetsEncryptOptions.CertificateKeyFileName);

        #endregion

        #region Internal Methods — Directory

        /// <summary>
        /// Creates the certificate directory if it does not already exist and sets permissions to <c>0700</c> on Linux.
        /// Idempotent — safe to call multiple times.
        /// </summary>
        /// <remarks>
        /// <para><b>Exception handling:</b> <see cref="Directory.CreateDirectory(string)"/> can throw various exceptions
        /// depending on the failure mode. This method catches all exceptions and logs them at <see cref="LogLevel.Error"/>
        /// before rethrowing, ensuring that permission issues, path validation failures, and I/O errors are never silently
        /// ignored. Common exceptions include:</para>
        /// <list type="bullet">
        ///   <item><description><c>UnauthorizedAccessException</c> — caller lacks permission to create the directory or
        ///     write to the parent directory.</description></item>
        ///   <item><description><c>ArgumentException</c> — the path contains invalid characters or is empty.</description></item>
        ///   <item><description><c>PathTooLongException</c> — the path or a component exceeds platform limits.</description></item>
        ///   <item><description><c>DirectoryNotFoundException</c> — the parent directory does not exist and could not be
        ///     created (e.g. on a disconnected network drive).</description></item>
        ///   <item><description><c>NotSupportedException</c> — the path format is not supported on the current platform.</description></item>
        ///   <item><description><c>IOException</c> — the directory path points to a file instead of a directory, or a
        ///     general I/O error occurs.</description></item>
        /// </list>
        /// <para><b>Permission setting:</b> After successful directory creation, <see cref="FileIOUtilities.TrySetSecureDirectoryPermissions(string)"/>
        /// is called to restrict access to the owner on Linux (0700). Permission failures are logged at
        /// <see cref="LogLevel.Warning"/> and do not prevent host startup — the directory is usable even if permissions
        /// cannot be set (e.g. on Windows or when running in a container without capability escalation).</para>
        /// <para><b>Logging:</b> All paths are logged for diagnostics; no credentials are included.</para>
        /// </remarks>
        /// <exception cref="UnauthorizedAccessException">Thrown when the process lacks permission to create the directory.
        /// </exception>
        /// <exception cref="ArgumentException">Thrown when the path is empty or contains invalid characters.</exception>
        /// <exception cref="PathTooLongException">Thrown when the path or a component exceeds the platform's path length
        /// limit.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the parent directory does not exist or is inaccessible.
        /// </exception>
        /// <exception cref="NotSupportedException">Thrown when the path format is not supported on the current platform.
        /// </exception>
        /// <exception cref="IOException">Thrown when the path references an existing file, or a general I/O error occurs.
        /// </exception>
        internal void EnsureCertsDirectory()
        {
            try
            {
                _ = Directory.CreateDirectory(_certsDirectoryPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogDirectoryCreationFailed(ex, _certsDirectoryPath, "Access denied");
                throw;
            }
            catch (ArgumentException ex)
            {
                LogDirectoryCreationFailed(ex, _certsDirectoryPath, "Invalid path characters or empty path");
                throw;
            }
            catch (PathTooLongException ex)
            {
                LogDirectoryCreationFailed(ex, _certsDirectoryPath, "Path exceeds maximum length");
                throw;
            }
            catch (DirectoryNotFoundException ex)
            {
                LogDirectoryCreationFailed(ex, _certsDirectoryPath, "Parent directory not found or inaccessible");
                throw;
            }
            catch (NotSupportedException ex)
            {
                LogDirectoryCreationFailed(ex, _certsDirectoryPath, "Path format not supported on this platform");
                throw;
            }
            catch (IOException ex)
            {
                LogDirectoryCreationFailed(ex, _certsDirectoryPath, "Path references a file or I/O error occurred");
                throw;
            }

            try
            {
                Exception? permEx = FileIOUtilities.TrySetSecureDirectoryPermissions(_certsDirectoryPath);
                if (permEx is not null)
                    LogDirectoryPermissionFailed(permEx, _certsDirectoryPath);

                LogCertificateDirectory(_certsDirectoryPath);
            }
            catch (Exception ex)
            {
                LogDirectoryPermissionFailed(ex, _certsDirectoryPath);
                throw;
            }
        }

        #endregion

        #region Internal Methods — Certificate Disposal

        /// <summary>
        /// Disposes a certificate and explicitly deletes its private key from the Windows CNG key store to prevent orphaned
        /// keys from accumulating across renewal cycles.
        /// </summary>
        /// <remarks>
        /// <para><b>Why explicit key deletion is needed:</b> When <see cref="X509KeyStorageFlags.PersistKeySet"/> is used
        /// (required on Windows for SChannel TLS server authentication), <see cref="X509Certificate2.Dispose"/> releases
        /// the in-memory handle but does <em>not</em> delete the persisted CNG key from
        /// <c>%APPDATA%\Microsoft\Crypto\Keys</c>.  Without explicit deletion, every renewal cycle (~60 days) orphans a
        /// key file that is never cleaned up.</para>
        ///
        /// <para><b>Mechanism:</b> Calling <see cref="X509Certificate2.GetECDsaPrivateKey"/> (or the RSA equivalent)
        /// returns an <see cref="AsymmetricAlgorithm"/> backed by a CNG key handle.  Disposing this handle triggers CNG
        /// key deletion when the key was opened from the persisted store.</para>
        ///
        /// <para><b>Algorithm detection:</b> The project uses ES256 (ECDSA P-256) keys via
        /// <see cref="AcmeCertificateProvider"/>, so <see cref="X509Certificate2.GetECDsaPrivateKey"/> is tried first.
        /// <see cref="X509Certificate2.GetRSAPrivateKey"/> is tried as a fallback for forward compatibility.</para>
        ///
        /// <para><b>Null return handling:</b> <see cref="X509Certificate2.GetECDsaPrivateKey"/> and
        /// <see cref="X509Certificate2.GetRSAPrivateKey"/> return <see langword="null"/> (not throw) when the certificate
        /// does not contain a private key of the requested algorithm type.  <c>using (null) { }</c> is a safe
        /// no-op.</para>
        ///
        /// <para><b>Linux:</b> On Linux, <see cref="X509KeyStorageFlags.EphemeralKeySet"/> keeps the key in memory only —
        /// there is nothing to delete.  The <see cref="OperatingSystem.IsWindows"/> guard avoids unnecessary calls on
        /// Linux.</para>
        ///
        /// <para><b>Best-effort with optional diagnostics:</b> Key deletion failures are swallowed — the certificate is
        /// disposed regardless.  When a <paramref name="logger"/> is provided, the exception is logged at
        /// <see cref="LogLevel.Debug"/>.  When <see langword="null"/> (the <see cref="DeferDisposal"/> fire-and-forget
        /// path), failures are silently swallowed.</para>
        ///
        /// <para><b>Known double-dispose:</b> During certificate rotation, both
        /// <see cref="CertificateRenewalService.ActivateCertificate"/> and
        /// <see cref="Nntp.NntpListener.OnCertificateChanged"/> may schedule deferred disposal of the same certificate.
        /// The second call's <see cref="X509Certificate2.GetECDsaPrivateKey"/> throws
        /// <see cref="CryptographicException"/> on the already-disposed certificate — caught by the best-effort
        /// <c>catch</c> block.  <see cref="X509Certificate2.Dispose"/> itself is idempotent on .NET 8.</para>
        ///
        /// <para><b>Thread safety:</b> This is a <see langword="static"/> method with no shared mutable state — safe for
        /// concurrent calls from any thread.</para>
        ///
        /// <para><b>Cross-platform:</b> The <see cref="OperatingSystem.IsWindows"/> guard ensures CNG key cleanup is only
        /// attempted on Windows.  On Linux, only the standard <see cref="X509Certificate2.Dispose"/> is called.</para>
        ///
        /// <para><b>Callers:</b></para>
        /// <list type="bullet">
        ///   <item><description><see cref="TryLoadCachedCertificate"/> — expired cached certificate.</description></item>
        ///   <item><description><see cref="DeferDisposal"/> — deferred disposal callback for superseded
        ///     certificates.</description></item>
        ///   <item><description><see cref="CertificateRenewalService.Dispose"/> — final cleanup during host
        ///     shutdown.</description></item>
        ///   <item><description><c>CertificateClusterSync</c>.TryAdoptPeerCertificateAsync"/> — rejected peer
        ///     certificate.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="cert">The certificate to dispose.  Must not be <see langword="null"/>.  The caller must not use
        /// this reference after the method returns.</param>
        /// <param name="logger">Optional logger for diagnostic output.  When <see langword="null"/>, CNG key cleanup
        /// failures are silently swallowed.</param>
        internal static void DisposeCertificate(X509Certificate2 cert, ILogger? logger = null)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using (cert.GetECDsaPrivateKey()) { }
                    using (cert.GetRSAPrivateKey()) { }
                }
                catch (Exception ex)
                {
                    if (logger is not null)
                        LogCngKeyCleanupFailed(logger, ex);
                }
            }

            cert.Dispose();
        }

        /// <summary>
        /// Schedules deferred disposal of a superseded <see cref="X509Certificate2"/> after the specified delay,
        /// allowing in-flight TLS handshakes referencing the old certificate to complete gracefully.
        /// </summary>
        /// <remarks>
        /// <para><b>Why deferred:</b> Active <see cref="System.Net.Security.SslStream"/> sessions may still reference
        /// the old certificate for in-progress TLS handshakes.  Disposing immediately would cause
        /// <see cref="System.Security.Authentication.AuthenticationException"/> or <see cref="CryptographicException"/>
        /// for any handshake that started before the swap but completes after.</para>
        ///
        /// <para><b>Allocation-free continuation:</b> Uses the
        /// <see cref="Task.ContinueWith{TResult}(Func{Task, object?, TResult}, object?, CancellationToken, TaskContinuationOptions, TaskScheduler)"/>
        /// overload with a <see langword="static"/> lambda and explicit <paramref name="old"/> state-passing to avoid a
        /// compiler-generated closure allocation.</para>
        ///
        /// <para><b><see cref="TaskContinuationOptions.ExecuteSynchronously"/>:</b> The continuation's work is trivial
        /// (one <see cref="DisposeCertificate"/> call) and completes in microseconds.  Running it synchronously on the
        /// timer callback thread avoids an unnecessary thread-pool hop.</para>
        ///
        /// <para><b>Continuation <see cref="CancellationToken.None"/>:</b> The continuation itself is scheduled with
        /// <see cref="CancellationToken.None"/> to ensure it always runs.  The
        /// <see cref="TaskStatus.RanToCompletion"/> guard <em>inside</em> the continuation prevents disposal during
        /// shutdown.  If the continuation's token were linked to the stopping token, the continuation would be
        /// unscheduled entirely on cancellation, orphaning the <see cref="X509Certificate2"/> until GC finalisation —
        /// which does <em>not</em> delete CNG keys on Windows.</para>
        ///
        /// <para><b>Completion guard:</b> The continuation checks <c>t.Status == TaskStatus.RanToCompletion</c> rather
        /// than <c>!t.IsCanceled</c>.  If <see cref="Task.Delay(TimeSpan, CancellationToken)"/> faults,
        /// <c>!IsCanceled</c> would still be <see langword="true"/> and proceed with disposal.
        /// <c>RanToCompletion</c> only allows disposal when the grace period genuinely elapsed.</para>
        ///
        /// <para><b>Null guard:</b> Returns immediately when <paramref name="old"/> is <see langword="null"/> (first
        /// certificate activation).</para>
        ///
        /// <para><b>Cross-platform:</b> Fully portable.  <see cref="Task.Delay(TimeSpan, CancellationToken)"/> and
        /// <see cref="Task.ContinueWith{TResult}(Func{Task, object?, TResult}, object?, CancellationToken, TaskContinuationOptions, TaskScheduler)"/>
        /// are BCL APIs available on all .NET 8 runtimes.  <see cref="DisposeCertificate"/> handles platform-specific CNG
        /// cleanup internally.</para>
        ///
        /// <para><b>Callers:</b></para>
        /// <list type="bullet">
        ///   <item><description><see cref="CertificateRenewalService.DeferCertificateDisposal"/> — superseded certificate
        ///     after <see cref="Interlocked.Exchange{T}"/>.</description></item>
        ///   <item><description><see cref="Nntp.NntpListener.OnCertificateChanged"/> — the previous
        ///     <c>_tlsCertificate</c> swapped out when the
        ///     <see cref="CertificateRenewalService.CertificateChanged"/> event fires.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="old">The certificate to dispose.  If <see langword="null"/>, no action is taken.</param>
        /// <param name="delay">Grace period before disposal.  Typically 5 minutes.</param>
        /// <param name="cancellationToken">Cancellation token linked to the host's stopping token.</param>
        internal static void DeferDisposal(X509Certificate2? old, TimeSpan delay, CancellationToken cancellationToken)
        {
            if (old is null)
                return;

            _ = Task.Delay(delay, cancellationToken).ContinueWith(
                static (t, state) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        DisposeCertificate((X509Certificate2)state!);
                },
                old,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        #endregion
    }

}
