// <copyright file="CertificateDefaults.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateDefaults.cs — Shared constants and defaults for the certificate subsystem.
//
// Centralises values used across multiple classes in the Certificates namespace, eliminating duplication and ensuring
// consistency.
//
// Members:
//   JsonOptions        — Frozen JsonSerializerOptions (CamelCase, source-generated CertificateJsonContext) for
//                        Cloudflare API payloads.
//   PfxKeyStorageFlags — Platform-aware X509KeyStorageFlags for PFX import (EphemeralKeySet on Linux, UserKeySet +
//                        PersistKeySet on Windows).
//
// Callers:
//   AcmeCertificateProvider  — JsonOptions for Cloudflare API serialisation; PfxKeyStorageFlags for PFX import after
//                              ACME order finalisation.
//   CertificateStore         — PfxKeyStorageFlags for loading cached certificates from disk at startup.
//
// Cross-platform:  Fully portable.  OperatingSystem.IsWindows() is the sole platform guard, evaluated once at
//                  type-load time.  No P/Invoke, no OS-specific APIs beyond the BCL runtime checks.
//
// SIMD applicability:  Not applicable.  This class holds two static readonly fields — no contiguous memory buffers,
//                      no bulk data processing, and no vectorisable computation.

using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vector.NNTP.Encryption.Certificates
{

    /// <summary>
    /// Shared defaults and constants for the certificate subsystem.  Centralises values that would otherwise be duplicated
    /// across <see cref="AcmeCertificateProvider"/> and <see cref="CertificateStore"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread safety:</b> All members are <see langword="static"/> <see langword="readonly"/> and immutable after
    /// type initialisation.  <see cref="JsonOptions"/> is explicitly frozen via
    /// <see cref="JsonSerializerOptions.MakeReadOnly(bool)"/> at construction time.  Safe for concurrent access from any
    /// thread without synchronisation.</para>
    ///
    /// <para><b>Two concerns:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="JsonOptions"/> — frozen <see cref="JsonSerializerOptions"/> backed by the
    ///     source-generated <see cref="CertificateJsonContext"/> with <see cref="JsonNamingPolicy.CamelCase"/> naming.
    ///     Used for Cloudflare REST API payloads.</description></item>
    ///   <item><description><see cref="PfxKeyStorageFlags"/> — platform-aware <see cref="X509KeyStorageFlags"/> evaluated
    ///     once at type-load time via <see cref="OperatingSystem.IsWindows"/>.  Ensures PFX certificates are imported with
    ///     the correct key storage mode for the host OS's TLS provider (OpenSSL on Linux, SChannel on
    ///     Windows).</description></item>
    /// </list>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  <see cref="OperatingSystem.IsWindows"/> is the sole platform guard.
    /// No P/Invoke, no architecture-specific intrinsics.  Compatible with Windows (x64) and Linux (x64) on
    /// .NET 8.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  This class holds two static readonly fields — no contiguous
    /// memory buffers, no bulk data processing, and no vectorisable computation.</para>
    /// </remarks>
    internal static class CertificateDefaults
    {
        #region JSON Serialisation

        /// <summary>
        /// Shared <see cref="JsonSerializerOptions"/> using <see cref="JsonNamingPolicy.CamelCase"/> for consistent JSON
        /// serialisation across the certificate subsystem.
        /// </summary>
        /// <remarks>
        /// <para><b>Callers:</b></para>
        /// <list type="bullet">
        ///   <item><description><see cref="AcmeCertificateProvider"/> — Cloudflare DNS API payloads (<c>POST
        ///     /dns_records</c>).</description></item>
        /// </list>
        ///
        /// <para><b>Naming policy:</b> <see cref="JsonNamingPolicy.CamelCase"/> maps <c>PascalCase</c> C# property names
        /// to <c>camelCase</c> JSON keys in both directions.  This matches the Cloudflare REST API convention.</para>
        ///
        /// <para><b>Null handling:</b> <see cref="JsonIgnoreCondition.WhenWritingNull"/> omits properties with
        /// <see langword="null"/> values from the serialised output, producing smaller payloads.  Current payload types
        /// default all properties to non-null values (<see cref="string.Empty"/>, <c>0</c>), so this is a forward-looking
        /// guard for any future nullable additions.</para>
        ///
        /// <para><b>Case sensitivity:</b> <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> is explicitly set
        /// to <see langword="false"/> (the default) to enforce strict camelCase wire-format matching.</para>
        ///
        /// <para><b>Indentation:</b> <see cref="JsonSerializerOptions.WriteIndented"/> is explicitly set to
        /// <see langword="false"/> (the default) to ensure compact single-line output.</para>
        ///
        /// <para><b>Source-generated type resolver:</b> <see cref="CertificateJsonContext.Default"/> is assigned as the
        /// <see cref="JsonSerializerOptions.TypeInfoResolver"/>, providing compile-time JSON metadata for all serialised
        /// types.  This eliminates reflection-based metadata resolution at runtime.  The application disables
        /// reflection-based JSON serialisation via <c>JsonSerializerIsReflectionEnabledByDefault=false</c> — without this
        /// explicit resolver, <see cref="JsonSerializerOptions.MakeReadOnly(bool)"/> with
        /// <c>populateMissingResolver: false</c> would freeze the options without any type metadata, and subsequent
        /// serialisation calls would throw <see cref="NotSupportedException"/>.</para>
        ///
        /// <para><b>Frozen:</b> <see cref="JsonSerializerOptions.MakeReadOnly(bool)"/> is called with
        /// <c>populateMissingResolver: false</c> immediately after construction.  The <c>false</c> parameter is correct
        /// because the source-generated <see cref="CertificateJsonContext"/> already provides all required type metadata at
        /// compile time — no reflection fallback is needed or available.  Freezing the instance after construction prevents
        /// accidental mutation and enables internal caching optimisations within
        /// <see cref="System.Text.Json"/>.</para>
        ///
        /// <para><b>Performance:</b> Reusing a single frozen, source-generated instance avoids repeated metadata resolution
        /// and serialiser cache allocation per call.  Thread-safe for concurrent reads after construction.</para>
        /// </remarks>
        internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        /// <summary>
        /// Creates and freezes the shared <see cref="JsonSerializerOptions"/> instance.  Extracted to a method because
        /// <see cref="JsonSerializerOptions.MakeReadOnly(bool)"/> returns <see langword="void"/> and cannot be chained in
        /// an object initialiser.
        /// </summary>
        /// <returns>A frozen <see cref="JsonSerializerOptions"/> instance backed by
        /// <see cref="CertificateJsonContext"/>.</returns>
        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                TypeInfoResolver = CertificateJsonContext.Default
            };

            // populateMissingResolver: false — the source-generated CertificateJsonContext provides all required type
            // metadata at compile time.  No reflection fallback is needed or available (reflection-based serialisation
            // is disabled via JsonSerializerIsReflectionEnabledByDefault=false).
            options.MakeReadOnly(populateMissingResolver: false);
            return options;
        }

        #endregion

        #region PFX Key Storage

        /// <summary>
        /// Platform-aware <see cref="X509KeyStorageFlags"/> for importing PFX certificates that will be used with
        /// <see cref="System.Net.Security.SslStream"/> for TLS server authentication.
        /// </summary>
        /// <remarks>
        /// <para><b>Callers:</b></para>
        /// <list type="bullet">
        ///   <item><description><see cref="AcmeCertificateProvider"/> — imports the newly-issued PFX after ACME order
        ///     download.</description></item>
        ///   <item><description><see cref="CertificateStore.TryLoadCachedCertificate"/> — loads the cached PFX from disk
        ///     at startup.</description></item>
        /// </list>
        ///
        /// <para><b>Linux (OpenSSL):</b> <see cref="X509KeyStorageFlags.EphemeralKeySet"/> keeps the private key in memory
        /// only — no disk I/O, no key store pollution.  OpenSSL handles ephemeral keys natively for TLS server
        /// authentication via <c>SslStream.AuthenticateAsServerAsync</c>.</para>
        ///
        /// <para><b>Windows (SChannel):</b> <see cref="X509KeyStorageFlags.UserKeySet"/> |
        /// <see cref="X509KeyStorageFlags.PersistKeySet"/> stores the private key in the current user's CNG key store
        /// (<c>%APPDATA%\Microsoft\Crypto</c>).  SChannel requires the key to be accessible via a CSP/CNG key storage
        /// provider for server authentication.</para>
        ///
        /// <para><b>Why <c>UserKeySet</c> instead of <c>MachineKeySet</c>:</b> The machine key store
        /// (<c>C:\ProgramData\Microsoft\Crypto\Keys</c>) requires administrative privileges or explicit ACL grants.
        /// <c>UserKeySet</c> works without elevation for both interactive development and Windows Services (which run
        /// under a service account with a user profile).</para>
        ///
        /// <para><b>Why <c>PersistKeySet</c>:</b> Without this flag, the private key is deleted from the CNG store when
        /// the <see cref="X509Certificate2"/> is garbage collected.  Because the certificate is held as a long-lived
        /// reference in <see cref="CertificateRenewalService"/> and served to every TLS handshake via
        /// <c>ServerCertificateSelectionCallback</c>, the key must remain in the store for the lifetime of the certificate
        /// object.</para>
        ///
        /// <para><b>CNG key cleanup:</b> The <see cref="X509KeyStorageFlags.PersistKeySet"/> flag means CNG keys are
        /// <em>not</em> automatically deleted when the <see cref="X509Certificate2"/> is disposed.
        /// <see cref="CertificateStore.DisposeCertificate"/> explicitly retrieves and disposes the private key handle to
        /// trigger CNG key deletion, preventing orphaned keys from accumulating in
        /// <c>%APPDATA%\Microsoft\Crypto\Keys</c> across renewal cycles.  Additionally,
        /// <see cref="CertificateRenewalService.ActivateCertificate"/> performs synchronous CNG key cleanup of the
        /// superseded certificate immediately after the atomic swap — before the deferred disposal timer — to ensure
        /// deterministic cleanup even if the process restarts before the 5-minute deferred timer fires.</para>
        ///
        /// <para><b>Evaluated once:</b> <see cref="OperatingSystem.IsWindows"/> is checked at type-load time.  The result
        /// is constant for the lifetime of the process.</para>
        /// </remarks>
        internal static readonly X509KeyStorageFlags PfxKeyStorageFlags = OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

        #endregion
    }

}
