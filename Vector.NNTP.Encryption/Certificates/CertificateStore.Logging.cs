// <copyright file="CertificateStore.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateStore.Logging.cs — Source-generated [LoggerMessage] partial methods for all structured log messages across
// the CertificateStore partial files.
//
// Uses the [LoggerMessage] source generator pattern mandated by CONTRIBUTING.md for compile-time validation,
// zero-allocation logging, and consistent structure.  The primary constructor's `logger` parameter is assigned to the
// primary constructor `logger` parameter in CertificateStore.cs, which the source generator uses for dispatch.
//
// Performance:
//   Source-generated logging methods avoid per-call string formatting, value-type boxing, and params object[] allocation.
//   The built-in IsEnabled guard skips message formatting entirely when the target log level is disabled.
//
// Callers (by partial file):
//   CertificateStore.cs             — LogDirectoryPermissionFailed, LogCertificateDirectory, LogCngKeyCleanupFailed.
//   CertificateStore.Persistence.cs — LogLoadedCachedCertificate, LogCachedCertificateExpired,
//                                     LogCachedCertificateCorrupt, LogCachedCertificateLoadFailed,
//                                     LogCertificateSaved, LogAccountKeySaved, LogCertificateKeySaved,
//                                     LogFileReadFailed.
//
// Event ID allocation:
//   300–302  Directory management and disposal  (CertificateStore.cs)
//   305–312  Persistence and file reads         (CertificateStore.Persistence.cs)
//
// Log level policy (aligned with CONTRIBUTING.md Log Levels):
//   Information  — Operator-visible milestones: cached certificate loaded, file saved.
//   Warning      — Recoverable failures: cached certificate expired, corrupt PFX, load/read failures.
//   Debug        — Diagnostic detail: directory permissions, certificate directory path, CNG key cleanup failures.
//
// ASCII-only log messages:
//   All Message strings use only ASCII characters (U+0020-U+007E) per CONTRIBUTING.md.  Em-dashes are replaced with
//   " -- " in all message templates.
//
// Security:
//   No method logs private key material, PFX bytes, or ACME account key content.  Thumbprints are SHA-1 hashes of
//   the public certificate — not sensitive.  File paths logged are the well-known certs/ directory locations.

using System.Security.Cryptography.X509Certificates;
using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Encryption.Certificates
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> partial methods for <see cref="CertificateStore"/>.
    /// </summary>
    /// <remarks>
    /// Event IDs 300–312; see file header for caller mapping and security constraints.
    /// </remarks>
    internal sealed partial class CertificateStore
    {
        #region Logging Methods — Directory Management and Disposal (300–302)

        /// <summary>
        /// Logs that setting directory permissions to <c>0700</c> on Linux failed.  The directory will be used with its
        /// default permissions.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="EnsureCertsDirectory"/> — after
        /// <see cref="FileIOUtilities.TrySetSecureDirectoryPermissions(string)"/> returns a non-null exception.</para>
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Debug"/> because the failure is non-actionable in most
        /// deployments (filesystem may not support Unix modes).</para>
        /// </remarks>
        [LoggerMessage(EventId = 300, Level = LogLevel.Debug,
            Message = "Certificates: Could not set directory permissions on {Path} -- continuing with default permissions")]
        private partial void LogDirectoryPermissionFailed(Exception ex, string path);

        /// <summary>
        /// Logs the resolved certificate directory path at startup.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="EnsureCertsDirectory"/> — after <see cref="Directory.CreateDirectory(string)"/> and
        /// the optional permission call complete.</para>
        /// </remarks>
        [LoggerMessage(EventId = 301, Level = LogLevel.Debug,
            Message = "Certificates: Certificate directory: {Path}")]
        private partial void LogCertificateDirectory(string path);

        /// <summary>
        /// Logs that CNG private key cleanup failed during certificate disposal on Windows.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="DisposeCertificate"/> — in the <c>catch (Exception)</c> block after
        /// <see cref="ECDsaCertificateExtensions.GetECDsaPrivateKey(X509Certificate2)"/> or
        /// <see cref="RSACertificateExtensions.GetRSAPrivateKey(X509Certificate2)"/> throws during
        /// key handle retrieval or disposal.</para>
        ///
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Debug"/> because the most common cause is the expected
        /// double-dispose race during certificate rotation.</para>
        ///
        /// <para><b>Static method with explicit logger parameter:</b> <see cref="DisposeCertificate"/> is
        /// <see langword="static"/> and cannot access the instance <c>_logger</c> field.  The <see cref="ILogger"/> is
        /// passed by callers that have one available; the <see cref="DeferDisposal"/> fire-and-forget path passes
        /// <see langword="null"/> and skips logging entirely.</para>
        /// </remarks>
        [LoggerMessage(EventId = 302, Level = LogLevel.Debug,
            Message = "Certificates: CNG private key cleanup failed during certificate disposal -- key may be orphaned")]
        private static partial void LogCngKeyCleanupFailed(ILogger logger, Exception ex);

        #endregion

        #region Logging Methods — Persistence (305–312)

        /// <summary>
        /// Logs that a cached certificate was successfully loaded from disk with its subject, thumbprint, and expiry.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="TryLoadCachedCertificate"/> — when the PFX file exists, parses successfully,
        /// and has not yet expired.</para>
        /// <para><b>Security:</b> The thumbprint is a SHA-1 hash of the public certificate — not sensitive.  The subject
        /// is the certificate's Common Name — public information visible in any TLS handshake.</para>
        /// </remarks>
        [LoggerMessage(EventId = 305, Level = LogLevel.Information,
            Message = "Certificates: Loaded cached certificate: Subject={Subject}, Thumbprint={Thumbprint}, Expires={NotAfter:yyyy/MM/dd HH:mm:ss}")]
        private partial void LogLoadedCachedCertificate(string subject, string thumbprint, DateTime notAfter);

        /// <summary>
        /// Logs that the cached certificate on disk has expired and will be replaced by a new ACME renewal.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="TryLoadCachedCertificate"/> — when the PFX file exists and parses successfully
        /// but <c>NotAfter</c> is in the past.  The expired certificate is disposed via <see cref="DisposeCertificate"/>
        /// immediately after this log.</para>
        /// </remarks>
        [LoggerMessage(EventId = 306, Level = LogLevel.Warning,
            Message = "Certificates: Cached certificate expired on {NotAfter:yyyy/MM/dd HH:mm:ss} -- will request new one")]
        private partial void LogCachedCertificateExpired(DateTime notAfter);

        /// <summary>
        /// Logs that the cached certificate file contains invalid PKCS#12 data — the PFX is corrupt and will be replaced
        /// by a new ACME renewal.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="TryLoadCachedCertificate"/> — in the <c>catch (CryptographicException)</c>
        /// block when the PFX file contains invalid PKCS#12 data.</para>
        ///
        /// <para><b>Distinction from <see cref="LogCachedCertificateLoadFailed"/>:</b> A
        /// <see cref="System.Security.Cryptography.CryptographicException"/> indicates the file content is not valid
        /// PKCS#12 — a permanent condition.  General I/O errors may be transient.</para>
        /// </remarks>
        [LoggerMessage(EventId = 307, Level = LogLevel.Warning,
            Message = "Certificates: Cached certificate at {Path} is corrupt -- will request new one")]
        private partial void LogCachedCertificateCorrupt(Exception ex, string path);

        /// <summary>
        /// Logs that loading the cached certificate from disk failed due to a permission issue, disk I/O failure, or other
        /// unexpected error.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="TryLoadCachedCertificate"/> — in the final <c>catch (Exception)</c> block.
        /// Reached only for exceptions not caught by the preceding specific handlers
        /// (<see cref="OperationCanceledException"/>, <see cref="FileNotFoundException"/>,
        /// <see cref="DirectoryNotFoundException"/>,
        /// <see cref="System.Security.Cryptography.CryptographicException"/>).</para>
        /// <para><b>Typical exceptions:</b> <see cref="UnauthorizedAccessException"/> (permissions),
        /// <see cref="IOException"/> (disk failure).</para>
        /// </remarks>
        [LoggerMessage(EventId = 308, Level = LogLevel.Warning,
            Message = "Certificates: Cached certificate load failed from {Path}")]
        private partial void LogCachedCertificateLoadFailed(Exception ex, string path);

        /// <summary>
        /// Logs that a PFX certificate was successfully persisted to disk.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="SaveCertificateAsync"/> — after
        /// <see cref="FileIOUtilities.AtomicWriteAsync"/> completes.</para>
        /// </remarks>
        [LoggerMessage(EventId = 309, Level = LogLevel.Information,
            Message = "Certificates: Certificate saved to {Path}")]
        private partial void LogCertificateSaved(string path);

        /// <summary>
        /// Logs that the ACME account private key was successfully persisted to disk.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="SaveAccountKeyAsync"/> — after
        /// <see cref="FileIOUtilities.AtomicWriteAsync"/> completes.</para>
        /// <para><b>Security:</b> Only the file path is logged — the PEM key content is never included.</para>
        /// </remarks>
        [LoggerMessage(EventId = 310, Level = LogLevel.Information,
            Message = "Certificates: ACME account key saved to {Path}")]
        private partial void LogAccountKeySaved(string path);

        /// <summary>
        /// Logs that the certificate private key was successfully persisted to disk.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="SaveCertificateKeyAsync"/> — after
        /// <see cref="FileIOUtilities.AtomicWriteAsync"/> completes.</para>
        /// <para><b>Security:</b> Only the file path is logged — the PEM key content is never included.</para>
        /// </remarks>
        [LoggerMessage(EventId = 311, Level = LogLevel.Information,
            Message = "Certificates: Certificate key saved to {Path}")]
        private partial void LogCertificateKeySaved(string path);

        /// <summary>
        /// Logs that a resilient file read failed with an unexpected I/O error.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="FileIOUtilities.TryReadFileAsync{T}"/> error callback — invoked
        /// for exceptions not caught by the preceding specific handlers (<see cref="FileNotFoundException"/>,
        /// <see cref="DirectoryNotFoundException"/>, <see cref="OperationCanceledException"/>).</para>
        /// <para><b>Parameters:</b> <c>{Description}</c> identifies the file type (e.g., "ACME account key",
        /// "certificate bytes").  <c>{Path}</c> is the absolute file path.</para>
        /// </remarks>
        [LoggerMessage(EventId = 312, Level = LogLevel.Warning,
            Message = "Certificates: {Description} load failed from {Path}")]
        private partial void LogFileReadFailed(Exception ex, string description, string path);

        #endregion

        /// <summary>
        /// Logs that directory creation or validation failed.
        /// </summary>
        /// <remarks>
        /// <para><b>Caller:</b> <see cref="EnsureCertsDirectory"/> — when <see cref="Directory.CreateDirectory(string)"/>
        /// throws any of the documented exceptions (permission denied, invalid path, path too long, parent not found,
        /// unsupported format, or I/O error).</para>
        /// <para><b>Level rationale:</b> <see cref="LogLevel.Error"/> because directory creation failure is fatal to
        /// certificate storage and requires immediate operator intervention.</para>
        /// </remarks>
        [LoggerMessage(EventId = 303, Level = LogLevel.Error,
            Message = "Certificates: Failed to create or access certificate directory {Path} ({ExceptionType})")]
        private partial void LogDirectoryCreationFailed(Exception ex, string path, string exceptionType);
    }

}
