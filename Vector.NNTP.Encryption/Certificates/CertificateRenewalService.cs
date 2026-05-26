// <copyright file="CertificateRenewalService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateRenewalService.cs — Background service for automatic TLS certificate provisioning and renewal via
// Let's Encrypt ACME DNS-01 challenge with Cloudflare DNS API.
//
// Partial files:
//   CertificateRenewalService.cs                  (this file) — constants, fields, properties, constructor, Dispose,
//                                                                public API (GetCurrentCertificate, CertificateChanged).
//   CertificateRenewalService.Lifecycle.cs        — ExecuteAsync, CheckAndRenewAsync, TryLoadCachedCertificate.
//   CertificateRenewalService.CertificateState.cs — ActivateCertificate, validity checks, event raising, deferred
//                                                   certificate disposal.
//   CertificateRenewalService.Logging.cs          — Source-generated [LoggerMessage] partial methods for all structured
//                                                   log messages across all partial files.
//
// Lifecycle:
//   Phase 1 — Load cached certificate from disk (instant TLS availability).
//   Phase 2 — Startup retry loop with exponential back-off until first certificate is obtained.
//   Phase 3 — Steady-state periodic renewal checks.
//
// Ownership and disposal model:
//   CertificateRenewalService is the sole owner of the certificate lifecycle.  When ActivateCertificate swaps the
//   current certificate, the service schedules deferred disposal of the superseded certificate via
//   DeferCertificateDisposal.  Subscribers of the CertificateChanged event receive the *new* certificate for their
//   own atomic swap but must NOT dispose the old certificate they swap out — it is the same object reference that
//   ActivateCertificate already captured and scheduled for disposal.  NntpListener.OnCertificateChanged and
//   PeerFetchListener.OnCertificateChanged both follow this contract: they swap their local _tlsCertificate reference
//   but do not dispose the previous value.
//
//   IMPORTANT: If a future subscriber is added, it must NOT call CertificateStore.DisposeCertificate or
//   CertificateStore.DeferDisposal on the old certificate it swaps out.  The renewal service owns disposal.
//
// SIMD applicability:
//   Not applicable.  The service orchestrates I/O-bound ACME protocol interactions and filesystem persistence.
//   There are no contiguous memory buffers, byte-level pattern searches, or bulk numeric operations that would
//   benefit from vector intrinsics.
//
// Cross-platform compatibility:
//   Fully compatible with Linux and Windows (ARM is not required).  Platform-specific behaviour is handled by
//   CertificateDefaults.PfxKeyStorageFlags (EphemeralKeySet on Linux, PersistKeySet on Windows) and
//   CertificateStore.DisposeCertificate (CNG key cleanup on Windows, no-op on Linux).

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vector.NNTP.Encryption.Certificates.Acme;
using Vector.NNTP.Encryption.Cluster;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Dns;
using Vector.NNTP.Utilities.Disposal;
using Vector.NNTP.Utilities.Retry;

namespace Vector.NNTP.Encryption.Certificates
{

    /// <summary>
    /// Background service that automatically provisions and renews TLS certificates from Let's Encrypt using the ACME
    /// DNS-01 challenge with Cloudflare DNS API for TXT record management.
    /// </summary>
    /// <remarks>
    /// <para><b>Environment isolation:</b> When running in the <c>Development</c> environment,
    /// <see cref="LetsEncryptOptions.UseStagingDirectory"/> is forced to <see langword="true"/> regardless of the
    /// configured value, ensuring development builds never consume Let's Encrypt production rate limits.</para>
    ///
    /// <para>On startup, the local disk cache (<c>certs/certificate.pfx</c>) provides instant TLS availability.  For
    /// truly fresh nodes with no cached certificate, the service proceeds directly to ACME renewal.</para>
    ///
    /// <para><b>Flow:</b></para>
    /// <code>
    ///   Startup
    ///     |
    ///     +-- Force UseStagingDirectory = true if Development environment
    ///     |
    ///     +-- certs/certificate.pfx exists &amp; valid &amp; not expiring soon?
    ///     |   +-- Yes -> load from disk, notify listener, skip ACME
    ///     |
    ///     +-- Certificate still needs renewal?
    ///     |   +-- No -> done (local cert is sufficient)
    ///     |   +-- Yes -> perform ACME renewal
    ///     |
    ///     +-- Resolve authoritative nameservers via Cloudflare zone API
    ///     |
    ///     +-- Request certificate via DNS-01
    ///     |   +-- New ACME order (domain names)
    ///     |   +-- For each DNS-01 challenge:
    ///     |   |   +-- Compute _acme-challenge TXT value
    ///     |   |   +-- POST TXT record via Cloudflare API
    ///     |   |   +-- Poll authoritative NS until TXT visible (~2-5 s)
    ///     |   |   +-- Validate challenge
    ///     |   +-- Finalize order -> build PFX
    ///     |   +-- Save PFX to certs/certificate.pfx (atomic via temp+rename)
    ///     |   +-- Cleanup TXT records
    ///     |   +-- Notify NntpListener via CertificateChanged
    ///     |
    ///     +-- Periodic renewal loop (every RenewalCheckIntervalHours)
    ///         +-- If certificate expires within RenewBeforeExpiryDays
    ///             +-- Request new certificate (same flow)
    /// </code>
    ///
    /// <para><b>Thread safety:</b> The current certificate is swapped atomically via
    /// <see cref="Interlocked.Exchange{T}(ref T, T)"/>.  <c>NntpListener</c> reads it via
    /// <see cref="GetCurrentCertificate"/> which uses <see cref="Volatile.Read{T}(ref T)"/> for cross-thread visibility.
    /// The <see cref="CertificateChanged"/> event is invoked with per-subscriber exception isolation so a faulting
    /// subscriber cannot break the renewal pipeline.</para>
    ///
    /// <para><b>Ownership model:</b> This service is the sole owner of certificate disposal.  When
    /// <see cref="ActivateCertificate"/> swaps the current certificate, the superseded certificate is scheduled for
    /// deferred disposal via <see cref="DeferCertificateDisposal"/>.  Subscribers of <see cref="CertificateChanged"/>
    /// receive the <em>new</em> certificate and must <b>not</b> dispose the old certificate they swap out of their own
    /// fields -- it is the same object reference that <see cref="ActivateCertificate"/> already captured and scheduled for
    /// disposal.  Double-disposing an <see cref="X509Certificate2"/> is safe on .NET 8, but on Windows the second
    /// <see cref="CertificateStore.DisposeCertificate"/> call would attempt <c>GetECDsaPrivateKey()</c> on an
    /// already-disposed certificate -- triggering a <see cref="System.Security.Cryptography.CryptographicException"/> that
    /// is silently swallowed by the best-effort <c>catch</c> block but wastes a CNG key-store lookup.</para>
    ///
    /// <para><b>Disposal:</b> Superseded certificates are not disposed immediately -- active
    /// <see cref="System.Net.Security.SslStream"/> sessions may still hold a reference for in-progress handshakes.
    /// Disposal is deferred by <see cref="OldCertificateDisposalDelay"/> (5 minutes) via a fire-and-forget
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> linked to <see cref="_stoppingToken"/> so timers are
    /// cancelled cleanly during shutdown.  All certificate disposal goes through
    /// <see cref="CertificateStore.DisposeCertificate"/> which explicitly deletes the persisted CNG key on Windows before
    /// calling <see cref="X509Certificate2.Dispose"/>, preventing orphaned keys from accumulating in
    /// <c>%APPDATA%\Microsoft\Crypto\Keys</c>.  <see cref="Dispose"/> atomically nulls and disposes the current
    /// certificate immediately for final cleanup.</para>
    ///
    /// <para><b>Double-dispose guard:</b> <see cref="Dispose"/> uses <see cref="Interlocked.Exchange(ref int, int)"/> on
    /// <see cref="_disposed"/> to ensure the disposal logic runs exactly once.  This follows the CONTRIBUTING.md §Resource
    /// Disposal pattern and prevents a second <see cref="Dispose"/> call (e.g. from the host's
    /// <see cref="IDisposable"/> sweep after <see cref="BackgroundService.Dispose"/>) from double-disposing the
    /// certificate or subsystems.</para>
    ///
    /// <para><b>Primary constructor parameters:</b></para>
    /// <list type="bullet">
    ///   <item><description><c>logger</c> -- <see cref="ILogger{TCategoryName}"/> scoped to this service.  Also passed to
    ///     <see cref="CertificateStore"/> and <see cref="AcmeCertificateProvider"/> so all certificate-related logs appear
    ///     under a single source context.</description></item>
    ///   <item><description><c>options</c> -- <see cref="IOptions{TOptions}"/> wrapping <see cref="LetsEncryptOptions"/>.
    ///     The <see cref="IOptions{TOptions}.Value"/> access is deferred to <see cref="ExecuteAsync"/> to ensure
    ///     <c>ValidateOnStart()</c> runs first.</description></item>
    ///   <item><description><c>nodeIdentity</c> -- provides the node's certificate directory path via
    ///     <see cref="LetsEncryptOptions.CertDir"/>.</description></item>
    ///   <item><description><c>hostLifetime</c> -- used to trigger <see cref="IHostApplicationLifetime.StopApplication"/>
    ///     on fatal configuration errors.</description></item>
    ///   <item><description><c>hostEnvironment</c> -- provides the hosting environment name for environment-aware staging
    ///     enforcement.</description></item>
    /// </list>
    /// </remarks>
    internal sealed partial class CertificateRenewalService(
        ILogger<CertificateRenewalService> logger,
        IOptions<LetsEncryptOptions> options,
        IOptions<NntpServerOptions> nntpServerOptions,
        IHostApplicationLifetime hostLifetime,
        IHostEnvironment hostEnvironment,
        IDnsTxtPropagationProbe dnsTxtProbe,
        IServiceProvider serviceProvider) : BackgroundService
    {
        #region Constants

        /// <summary>
        /// Base delay for exponential back-off when the service has no valid certificate and renewal fails.  Doubles on
        /// each consecutive failure up to <see cref="StartupRetryMaxDelayMs"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Overflow safety:</b> Delegated to <see cref="NetworkUtilities.CalculateBackOff"/> which casts the base
        /// delay to <see cref="long"/> before left-shifting, then caps via <see cref="Math.Min(long, long)"/> against
        /// <see cref="StartupRetryMaxDelayMs"/>.  The <c>MaxBackOffShift</c> (30) internal cap in
        /// <see cref="NetworkUtilities"/> prevents integer overflow for any <see cref="int"/>-range base delay.</para>
        /// </remarks>
        private const int StartupRetryBaseDelayMs = 30_000;

        /// <summary>
        /// Maximum delay between startup retry attempts (5 minutes).  Caps the exponential back-off to prevent excessive
        /// wait times while still providing breathing room for transient failures (DNS propagation, Let's Encrypt rate
        /// limits, network issues).
        /// </summary>
        private const int StartupRetryMaxDelayMs = 300_000;

        /// <summary>
        /// Grace period before disposing a superseded TLS certificate.  Five minutes is well beyond the longest possible
        /// TLS handshake timeout (~30 s), ensuring no in-flight <see cref="System.Net.Security.SslStream"/> session is
        /// disrupted.  Matches the delay used by <c>NntpListener</c> in <c>OnCertificateChanged</c>.
        /// </summary>
        private static readonly TimeSpan OldCertificateDisposalDelay = TimeSpan.FromMinutes(5);

        #endregion

        #region Fields

        /// <summary>
        /// Configured node name from <see cref="NntpServerOptions.NodeName"/> for logging and future cluster identity.
        /// </summary>
        private readonly string _nodeName = nntpServerOptions.Value.NodeName;

        /// <summary>
        /// Double-dispose guard.  Set to <c>1</c> by the first <see cref="Dispose"/> call via
        /// <see cref="Interlocked.Exchange(ref int, int)"/>.  Subsequent calls return immediately without executing the
        /// disposal logic.
        /// </summary>
        /// <remarks>
        /// <para><b>Pattern:</b> Follows CONTRIBUTING.md §Resource Disposal — <c>Interlocked.Exchange</c> for
        /// double-dispose guards.  Ensures the certificate and ACME provider are disposed exactly once, preventing a
        /// second <see cref="CertificateStore.DisposeCertificate"/> call from attempting
        /// <c>GetECDsaPrivateKey()</c> on an already-disposed certificate (which throws
        /// <see cref="System.Security.Cryptography.CryptographicException"/> on Windows).</para>
        /// </remarks>
        private int _disposed;

        /// <summary>
        /// Validated options snapshot, populated at the start of <see cref="ExecuteAsync"/> rather than in the field
        /// initializer.  Deferring the <see cref="IOptions{TOptions}.Value"/> access ensures <c>ValidateOnStart()</c> runs
        /// first, producing a clean <see cref="OptionsValidationException"/> through the host's standard startup pipeline
        /// rather than an opaque DI resolution failure during constructor injection.
        /// </summary>
        private LetsEncryptOptions _options = null!;

        /// <summary>
        /// The currently active TLS certificate.  Swapped atomically via <see cref="Interlocked.Exchange{T}(ref T, T)"/>
        /// in <see cref="ActivateCertificate"/> and read via <see cref="Volatile.Read{T}(ref T)"/> in
        /// <see cref="GetCurrentCertificate"/>.  <see langword="null"/> when no certificate has been provisioned yet
        /// (first startup, before the ACME round-trip or cached PFX load completes).
        /// </summary>
        private X509Certificate2? _currentCertificate;

        /// <summary>
        /// Host shutdown token captured from <see cref="ExecuteAsync"/> so that deferred certificate disposal timers in
        /// <see cref="DeferCertificateDisposal"/> are cancelled cleanly during shutdown rather than outliving the service.
        /// </summary>
        private CancellationToken _stoppingToken;

        /// <summary>
        /// Handles all ACME protocol interactions with Let's Encrypt: account management, DNS-01 challenge orchestration
        /// via Cloudflare, and order finalisation.  Created in <see cref="ExecuteAsync"/> once options are validated.
        /// <see langword="null"/> when Let's Encrypt is disabled.
        /// </summary>
        private AcmeCertificateProvider? _acmeProvider;

        /// <summary>
        /// Manages certificate and ACME key persistence on the local filesystem with atomic writes (temp file + rename).
        /// Created in <see cref="ExecuteAsync"/> once options are validated.  <see langword="null"/> when Let's Encrypt is
        /// disabled.
        /// </summary>
        private CertificateStore? _store;

        /// <summary>
        /// Optional cluster fanout coordinator when <see cref="LetsEncryptOptions.ClusterEnabled"/> is true.
        /// </summary>
        private CertificateClusterSync? _clusterSync;

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns the current TLS certificate, or <see langword="null"/> if none has been provisioned yet.  Thread-safe
        /// for concurrent access from <c>NntpListener</c> and internal validity-check methods.
        /// </summary>
        /// <remarks>
        /// <para>Uses <see cref="Volatile.Read{T}(ref T)"/> to guarantee that the value written by
        /// <see cref="ActivateCertificate"/> (via <see cref="Interlocked.Exchange{T}(ref T, T)"/>) on one thread is
        /// visible to reader threads -- the acquire fence prevents the CPU or compiler from reordering subsequent reads
        /// before this one.  Reference reads are atomic on all .NET platforms, so <see cref="Volatile.Read{T}(ref T)"/>
        /// adds only the memory-ordering guarantee.</para>
        /// </remarks>
        public X509Certificate2? GetCurrentCertificate()
            => Volatile.Read(ref _currentCertificate);

        /// <summary>
        /// Fired when a new certificate is activated -- either loaded from disk or renewed via ACME.  Subscribers are
        /// invoked with per-subscriber exception isolation so a faulting subscriber cannot break the renewal pipeline.
        /// </summary>
        /// <remarks>
        /// <para><b>Ownership contract:</b> The event passes the <em>new</em> certificate to subscribers.  Subscribers
        /// that maintain their own certificate reference (e.g. <c>NntpListener</c> TLS certificate field) should
        /// atomically swap it via <see cref="Interlocked.Exchange{T}(ref T, T)"/> but must <b>not</b> dispose the old
        /// certificate they swap out.  Disposal of superseded certificates is handled exclusively by this service via
        /// <see cref="DeferCertificateDisposal"/> -- the old certificate reference captured in
        /// <see cref="ActivateCertificate"/> is the same object that subscribers are swapping out of their own fields.
        /// Disposing it from a subscriber would cause a double-dispose.</para>
        ///
        /// <para><b>Subscribers:</b></para>
        /// <list type="bullet">
        ///   <item><description><c>NntpListener</c> -- atomically swaps the TLS certificate for subsequent
        ///     client-facing NNTPS handshakes and, on first arrival, late-binds the NNTPS listening
        ///     socket.</description></item>
        ///   <item><description><c>PeerFetchListener</c> -- atomically swaps the TLS certificate for
        ///     subsequent peer-to-peer cache-fetch handshakes and, on first arrival, builds the shared
        ///     <see cref="System.Net.Security.SslServerAuthenticationOptions"/> and signals the certificate-ready
        ///     <see cref="TaskCompletionSource"/> so <c>ExecuteAsync</c> can proceed to bind the peer listening
        ///     socket.</description></item>
        /// </list>
        ///
        /// <para><b>Disposal:</b> The event delegate is cleared to <see langword="null"/> during <see cref="Dispose"/>
        /// to release subscriber references and prevent post-disposal invocations.</para>
        /// </remarks>
        public event Action<X509Certificate2>? CertificateChanged;

        #endregion

        #region Dispose

        /// <summary>
        /// Releases all resources held by the service: the current certificate, the ACME provider, and the base
        /// <see cref="BackgroundService"/> timer.
        /// </summary>
        /// <remarks>
        /// <para><b>Double-dispose guard:</b> Uses <see cref="Interlocked.Exchange(ref int, int)"/> on
        /// <see cref="_disposed"/> to ensure the disposal logic runs exactly once.  If a second call arrives (e.g. the
        /// host's <see cref="IDisposable"/> sweep after the base class has already called <see cref="Dispose"/>), the
        /// method returns immediately.  This follows the CONTRIBUTING.md §Resource Disposal pattern.</para>
        ///
        /// <para><b>Disposal order:</b></para>
        /// <list type="number">
        ///   <item><description>Clear the <see cref="CertificateChanged"/> event delegate to release subscriber references
        ///     and prevent post-disposal invocations from any code path that races with
        ///     shutdown.</description></item>
        ///   <item><description>Atomically null and dispose <see cref="_currentCertificate"/> via
        ///     <see cref="CertificateStore.DisposeCertificate"/> -- prevents <see cref="GetCurrentCertificate"/> from
        ///     returning a disposed certificate to <c>NntpListener</c> and cleans up the persisted CNG key on
        ///     Windows.</description></item>
        ///   <item><description>Dispose <see cref="_acmeProvider"/> -- releases the <see cref="SemaphoreSlim"/> used for
        ///     one-time authoritative DNS resolution.  Best-effort via <see cref="DisposalUtilities.TryDispose"/> to
        ///     ensure a faulting dispose does not prevent subsequent cleanup.</description></item>
        ///   <item><description>Call <c>base.Dispose()</c> -- disposes the <see cref="BackgroundService"/>
        ///     timer.</description></item>
        ///   <item><description>Call <see cref="GC.SuppressFinalize(object)"/> -- prevents the GC from scheduling a
        ///     finalizer call for this object, matching the pattern used by <see cref="Nntp.NntpListener.Dispose"/>
        ///     and the standard <see cref="IDisposable"/> best practice.</description></item>
        /// </list>
        ///
        /// <para><b>Deferred disposal note:</b> Any certificate superseded during the exact moment of shutdown that has a
        /// pending <see cref="DeferCertificateDisposal"/> timer will have its timer cancelled (via
        /// <see cref="_stoppingToken"/>).  The <see cref="X509Certificate2"/> object will be collected by the GC -- this is
        /// acceptable because the process is shutting down.</para>
        ///
        /// <para><b>Subscriber race:</b> Between the <see cref="Interlocked.Exchange{T}(ref T, T)"/> that nulls
        /// <see cref="_currentCertificate"/> and the <see cref="CertificateStore.DisposeCertificate"/> call, a concurrent
        /// <see cref="GetCurrentCertificate"/> reader returns <see langword="null"/>.  Subscribers
        /// (<c>NntpListener</c>, <c>PeerFetchListener</c>) that hold their own
        /// <c>_tlsCertificate</c> reference still point to the certificate object that is about to be disposed.  This is
        /// safe because <see cref="Dispose"/> is called during host shutdown -- the listeners have already stopped accepting
        /// new connections, and the 5-minute <see cref="OldCertificateDisposalDelay"/> for any in-flight handshakes has
        /// either expired or is irrelevant since the process is terminating.</para>
        /// </remarks>
        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            CertificateChanged = null;

            X509Certificate2? cert = Interlocked.Exchange(ref _currentCertificate, null);
            if (cert is not null)
                CertificateStore.DisposeCertificate(cert, logger);

            DisposalUtilities.TryDispose(_acmeProvider);
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion
    }

}
