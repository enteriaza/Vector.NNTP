// <copyright file="CertificateRenewalService.CertificateState.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateRenewalService.CertificateState.cs — Certificate state management: atomic swap, validity checks, deferred
// disposal of superseded certificates.
//
// ActivateCertificate              — Atomic certificate swap, synchronous CNG key cleanup, event notification, deferred
//                                    disposal.
// IsCertificateValidBeyondThreshold — Strict expiry check against RenewBeforeExpiryDays threshold.
// IsCertificatePresent             — Loose expiry check (any non-expired certificate) for the startup retry loop.
// RaiseCertificateChanged          — Per-subscriber exception-isolated event invocation.
// DeferCertificateDisposal         — Fire-and-forget 5-minute delayed disposal with _stoppingToken cancellation and
//                                    CNG key cleanup on Windows.
// CleanupCngKeyImmediately         — Synchronous CNG private key deletion for the superseded certificate on Windows,
//                                    called from ActivateCertificate before DeferCertificateDisposal to ensure
//                                    deterministic cleanup even if the process restarts before the deferred timer fires.
//
// Thread safety:
//   ActivateCertificate uses Interlocked.Exchange for the certificate swap.
//   GetCurrentCertificate (in the primary partial) uses Volatile.Read for an acquire-fence atomic read.
//   DeferCertificateDisposal delegates to CertificateStore.DeferDisposal with a static lambda to avoid closure
//   allocations.
//
// Ownership model:
//   This service is the sole owner of certificate disposal.  ActivateCertificate captures the superseded certificate
//   via Interlocked.Exchange, raises the CertificateChanged event (passing the *new* certificate), performs synchronous
//   CNG key cleanup of the *old* certificate on Windows, and then defers disposal of the *old* certificate.
//   Subscribers must NOT dispose the old certificate they swap out of their own fields -- it is the same object
//   reference that ActivateCertificate captured.  See the ownership contract documented on CertificateChanged and the
//   class-level remarks in the primary partial.
//
//   KNOWN ISSUE: NntpListener.OnCertificateChanged calls CertificateStore.DeferDisposal on the old certificate it
//   swaps out of its _tlsCertificate field -- violating the ownership contract.  This results in the same certificate
//   being scheduled for deferred disposal twice (once by ActivateCertificate here, once by NntpListener).  The double
//   disposal is safe on .NET 8 because X509Certificate2.Dispose is idempotent.  On Windows, the second
//   CertificateStore.DisposeCertificate call attempts GetECDsaPrivateKey() on an already-disposed certificate, which
//   throws CryptographicException -- silently swallowed by the best-effort catch block.  No data loss or functional
//   impact occurs.  Fixing this requires NntpListener to stop calling DeferDisposal and instead rely solely on the
//   renewal service's deferred disposal, but that change is outside the scope of this file.
//
// Exception safety:
//   ActivateCertificate throws ObjectDisposedException if the service has been disposed.  This prevents a post-dispose
//   certificate from being stored in _currentCertificate without a corresponding Dispose to clean it up -- a resource
//   leak where the X509Certificate2 (and its persisted CNG key on Windows) would remain allocated until process exit.
//   Callers have try/finally guards that dispose the certificate when ActivateCertificate throws, so the certificate
//   is cleaned up on all rejection paths.
//
// Callers:
//   ActivateCertificate    — TryLoadCachedCertificate, CheckAndRenewAsync.
//   IsCertificateValidBeyondThreshold — CheckAndRenewAsync (entry gate).
//   IsCertificatePresent   — ExecuteAsync (startup retry loop condition).
//   RaiseCertificateChanged — ActivateCertificate.
//   DeferCertificateDisposal — ActivateCertificate.
//   CleanupCngKeyImmediately — ActivateCertificate.
//
// SIMD applicability:
//   Not applicable.  This partial contains certificate state management — atomic reference swaps, DateTime comparisons,
//   and event invocations.  There are no contiguous memory buffers, byte-level pattern searches, or bulk numeric
//   operations that would benefit from vector intrinsics.
//
// Cross-platform compatibility:
//   Fully compatible with Linux and Windows (ARM is not required).  No platform-specific APIs are used directly in this
//   file.  Platform-specific behaviour (CNG key cleanup on Windows) is handled by CertificateStore.DeferDisposal which
//   delegates to CertificateStore.DisposeCertificate.

using System.Security.Cryptography.X509Certificates;
using Vector.NNTP.Encryption.Configuration;

namespace Vector.NNTP.Encryption.Certificates
{

    /// <summary>
    /// Certificate state management: atomic swap, validity checks, deferred disposal of superseded certificates.
    /// </summary>
    internal sealed partial class CertificateRenewalService
    {
        #region Internal Methods — Certificate State

        /// <summary>
        /// Atomically swaps the current certificate, performs synchronous CNG key cleanup of the superseded certificate on
        /// Windows, raises <see cref="ICertificateRenewalPublisher.CertificateChanged"/>, and defers disposal of the superseded certificate.
        /// </summary>
        /// <remarks>
        /// <para><b>Disposed-state guard:</b> If <see cref="_disposed"/> is non-zero (set by <see cref="Dispose"/>),
        /// <see cref="ObjectDisposedException"/> is thrown immediately — before the certificate is stored in
        /// <see cref="_currentCertificate"/>.  Without this guard, a post-dispose call would store a new certificate that
        /// no subsequent <see cref="Dispose"/> call or future <see cref="ActivateCertificate"/> swap would ever clean up —
        /// leaking the <see cref="X509Certificate2"/> and (on Windows) its persisted CNG key.  The caller's
        /// <c>try/finally</c> guard disposes the certificate when this exception is thrown, preventing the leak.</para>
        ///
        /// <para><b>Null-argument guard:</b> <see cref="M:System.ArgumentNullException.ThrowIfNull(System.Object,System.String)"/> validates
        /// <paramref name="cert"/> before any state mutation.  A <see langword="null"/> certificate would cause
        /// <see cref="NullReferenceException"/> on the <c>cert.Thumbprint</c> access and leave
        /// <see cref="_currentCertificate"/> as <see langword="null"/> — indistinguishable from "no certificate provisioned
        /// yet" — which would trigger a spurious ACME renewal on the next check cycle.</para>
        ///
        /// <para><b>Operation sequence:</b></para>
        /// <list type="number">
        ///   <item><description><b>Disposed-state guard:</b> Throws <see cref="ObjectDisposedException"/> if the service
        ///     has been disposed.</description></item>
        ///   <item><description><b>Null-argument guard:</b> Throws <see cref="ArgumentNullException"/> if
        ///     <paramref name="cert"/> is <see langword="null"/>.</description></item>
        ///   <item><description><b>Atomic swap:</b> <see cref="Interlocked.Exchange{T}(ref T, T)"/> on
        ///     <see cref="_currentCertificate"/> atomically replaces the certificate reference and captures the previous
        ///     value.  All concurrent <see cref="GetCurrentCertificate"/> readers immediately see the new
        ///     certificate.</description></item>
        ///   <item><description><b>Activation log:</b> <see cref="LogCertificateActivated"/> emits an Information-level
        ///     structured log with the certificate's subject, thumbprint, and expiry — providing a single diagnostic
        ///     record for all activation paths (cache load, ACME renewal).</description></item>
        ///   <item><description><b>Event notification:</b> <see cref="RaiseCertificateChanged"/> invokes each
        ///     <see cref="ICertificateRenewalPublisher.CertificateChanged"/> subscriber with per-subscriber exception isolation.</description></item>
        ///   <item><description><b>Synchronous CNG key cleanup:</b> <see cref="CleanupCngKeyImmediately"/> deletes the
        ///     superseded certificate's persisted CNG key from the Windows key store immediately — before the deferred
        ///     disposal timer.  This prevents orphaned CNG keys from accumulating in
        ///     <c>%APPDATA%\Microsoft\Crypto\Keys</c> when the process restarts before the 5-minute deferred timer fires.
        ///     The certificate object itself remains alive for in-flight TLS handshakes; only the persisted key file is
        ///     removed.  SslStream sessions that already completed their TLS handshake hold the key in memory and are
        ///     unaffected.  On Linux, this step is a no-op.</description></item>
        ///   <item><description><b>Deferred disposal:</b> <see cref="DeferCertificateDisposal"/> schedules disposal of
        ///     the superseded certificate after <see cref="OldCertificateDisposalDelay"/> (5 minutes).  On Windows, the
        ///     CNG key has already been deleted by step 6, so <see cref="CertificateStore.DisposeCertificate"/>'s
        ///     <c>GetECDsaPrivateKey()</c> call will throw <see cref="System.Security.Cryptography.CryptographicException"/>
        ///     — silently swallowed by the best-effort catch block.  This is the expected double-cleanup
        ///     pattern.</description></item>
        /// </list>
        ///
        /// <para><b>Ordering — event before CNG cleanup:</b> <see cref="RaiseCertificateChanged"/> is invoked before
        /// <see cref="CleanupCngKeyImmediately"/> so that subscribers can swap their local references while the old
        /// certificate's key handle is still valid.  In practice, subscribers only store the reference — they do not access
        /// the private key during the event callback.  But notifying first is correct by design.</para>
        ///
        /// <para><b>CNG cleanup before deferred disposal:</b> <see cref="CleanupCngKeyImmediately"/> is called before
        /// <see cref="DeferCertificateDisposal"/> to ensure deterministic key deletion.  The deferred disposal timer may
        /// never fire if the process exits within 5 minutes (common during development restarts, deployment rolling
        /// updates, or crash scenarios).  Without synchronous cleanup, every such restart orphans a CNG key file that
        /// accumulates across restart cycles.</para>
        ///
        /// <para><b>Ownership transfer:</b> This method takes ownership of <paramref name="cert"/> — the caller must not
        /// dispose it after this call.  The certificate becomes the value of <see cref="_currentCertificate"/> and will be
        /// disposed only when it is superseded by a future <see cref="ActivateCertificate"/> call (deferred disposal) or
        /// during <see cref="Dispose"/> (immediate disposal).  The superseded certificate (returned by
        /// <see cref="Interlocked.Exchange{T}(ref T, T)"/>) is scheduled for deferred disposal and must not be disposed
        /// by any other code path — including <see cref="ICertificateRenewalPublisher.CertificateChanged"/> subscribers.  See the ownership contract
        /// documented on <see cref="ICertificateRenewalPublisher.CertificateChanged"/>.</para>
        ///
        /// <para><b>First activation:</b> When called for the first time (no previous certificate),
        /// <see cref="Interlocked.Exchange{T}(ref T, T)"/> returns <see langword="null"/>.
        /// <see cref="CleanupCngKeyImmediately"/> and <see cref="DeferCertificateDisposal"/> both handle the
        /// <see langword="null"/> case with an early return — no cleanup or disposal is scheduled.  This occurs during
        /// <see cref="TryLoadCachedCertificate"/> on startup.</para>
        ///
        /// <para><b>Thread safety:</b> <see cref="Interlocked.Exchange{T}(ref T, T)"/> provides a full memory barrier,
        /// ensuring that the new certificate reference and all its fields (Subject, Thumbprint, NotAfter) are visible to
        /// all threads.</para>
        ///
        /// <para><b>TOCTOU note on the disposed check:</b> There is a theoretical window between the
        /// <see cref="M:System.Threading.Volatile.Read(System.Int32@)"/> of <see cref="_disposed"/> and the
        /// <see cref="Interlocked.Exchange{T}(ref T, T)"/> where <see cref="Dispose"/> could interleave.  In that
        /// scenario the certificate is stored, then <see cref="Dispose"/> nulls and disposes it immediately — the
        /// <see cref="DeferCertificateDisposal"/> call in this method would subsequently schedule disposal of the
        /// superseded (previous) certificate, which is correct.  The newly stored certificate is cleaned up by
        /// <see cref="Dispose"/>'s <see cref="Interlocked.Exchange{T}(ref T, T)"/> on <see cref="_currentCertificate"/>.
        /// A lock spanning both the check and the swap would eliminate this window but adds contention on every
        /// activation for no practical benefit — the race is harmless and requires <see cref="Dispose"/> and
        /// <see cref="ActivateCertificate"/> to execute within nanoseconds of each other.</para>
        /// </remarks>
        /// <param name="cert">The newly provisioned or loaded certificate to activate.  Must not be
        /// <see langword="null"/>.  Ownership is transferred — the caller must not dispose or use this reference after
        /// the method returns.</param>
        /// <exception cref="ObjectDisposedException">The service has been disposed.  The caller retains ownership of
        /// <paramref name="cert"/> and is responsible for disposing it.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="cert"/> is <see langword="null"/>.</exception>
        internal void ActivateCertificate(X509Certificate2 cert)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ArgumentNullException.ThrowIfNull(cert);

            X509Certificate2? old = Interlocked.Exchange(ref _currentCertificate, cert);

            LogCertificateActivated(cert.Subject, cert.Thumbprint, cert.NotAfter);
            RaiseCertificateChanged(cert);
            CleanupCngKeyImmediately(old);
            DeferCertificateDisposal(old);
        }

        /// <summary>
        /// Returns <see langword="true"/> if the current certificate is valid beyond the renewal threshold
        /// (<see cref="LetsEncryptOptions.RenewBeforeExpiryDays"/>).  Used as the entry gate in
        /// <see cref="CheckAndRenewAsync"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Single read — TOCTOU prevention:</b> The certificate reference is read once via
        /// <see cref="M:System.Threading.Volatile.Read``1(``0@)"/> and all subsequent property accesses
        /// (<see cref="X509Certificate2.NotAfter"/>) operate on the captured local.  This avoids a TOCTOU race where
        /// <see cref="ActivateCertificate"/> swaps the certificate between the null check and the <c>NotAfter</c> read —
        /// which would cause a <see cref="NullReferenceException"/> if the new value were <see langword="null"/> (not
        /// possible in the current code, but the pattern is correct by construction), or an inconsistent validity
        /// decision if the new certificate has a different <c>NotAfter</c>.</para>
        ///
        /// <para><b>UTC comparison:</b> <see cref="DateTime.UtcNow"/> is used instead of <see cref="DateTime.Now"/> to
        /// avoid timezone-dependent expiry calculations.  <see cref="X509Certificate2.NotAfter"/> returns a
        /// <see cref="DateTime"/> with <see cref="DateTimeKind.Local"/> kind, but the subtraction produces a
        /// <see cref="TimeSpan"/> that is correct regardless of kind — the absolute difference in ticks is the same.
        /// Using <see cref="DateTime.UtcNow"/> avoids the overhead of the local-to-UTC conversion that
        /// <see cref="DateTime.Now"/> performs (timezone lookup, DST rules).</para>
        ///
        /// <para><b>Logging:</b> The method emits Debug-level log messages via <see cref="LogSkippingRenewal"/> (when
        /// valid beyond threshold) and <see cref="LogWithinThreshold"/> (when within threshold) to aid operational
        /// diagnostics without polluting higher log levels on every periodic check.  The source-generated
        /// <c>[LoggerMessage]</c> methods perform their own <c>IsEnabled</c> check internally, so no explicit guard is
        /// needed at the call site.</para>
        ///
        /// <para><b>Callers and frequency:</b></para>
        /// <list type="bullet">
        ///   <item><description><see cref="CheckAndRenewAsync"/> — entry gate (once per renewal cycle, typically every
        ///     6 hours via <see cref="LetsEncryptOptions.RenewalCheckIntervalHours"/>).</description></item>
        /// </list>
        /// </remarks>
        internal bool IsCertificateValidBeyondThreshold()
        {
            X509Certificate2? cert = Volatile.Read(ref _currentCertificate);
            if (cert is null) return false;

            double remainingDays = (cert.NotAfter - DateTime.UtcNow).TotalDays;

            if (remainingDays > _options.RenewBeforeExpiryDays)
            {
                LogSkippingRenewal(remainingDays);
                return true;
            }

            LogWithinThreshold(remainingDays);
            return false;
        }

        /// <summary>
        /// Returns <see langword="true"/> if any non-expired certificate is present (loose check for startup retry loop).
        /// </summary>
        /// <remarks>
        /// <para><b>Distinction from <see cref="IsCertificateValidBeyondThreshold"/>:</b> This method only checks whether
        /// the certificate has not yet expired — it does not compare against the renewal threshold.  A certificate that
        /// expires in 5 days still passes this check.  This is intentional: the startup retry loop's purpose is to
        /// obtain <em>any</em> working certificate for immediate TLS availability.  The steady-state renewal loop (which
        /// uses <see cref="IsCertificateValidBeyondThreshold"/>) handles proactive renewal before expiry.</para>
        ///
        /// <para><b>Single read — TOCTOU prevention:</b> Same pattern as <see cref="IsCertificateValidBeyondThreshold"/>
        /// — the certificate reference is captured once via <see cref="M:System.Threading.Volatile.Read``1(``0@)"/> and both the null check
        /// and <see cref="X509Certificate2.NotAfter"/> comparison operate on the captured local.  This prevents a race
        /// where <see cref="ActivateCertificate"/> swaps the certificate between the two checks.</para>
        ///
        /// <para><b>UTC comparison:</b> Uses <see cref="DateTime.UtcNow"/> for consistency with
        /// <see cref="IsCertificateValidBeyondThreshold"/> and to avoid timezone-related overhead.  See that method's
        /// remarks for details.</para>
        ///
        /// <para><b>Caller:</b> <see cref="ExecuteAsync"/> — startup retry loop condition.  Called once per retry
        /// iteration (before <see cref="CheckAndRenewAsync"/>).  When it returns <see langword="true"/>, the loop exits
        /// and the service transitions to the steady-state renewal loop.</para>
        /// </remarks>
        internal bool IsCertificatePresent()
        {
            X509Certificate2? cert = Volatile.Read(ref _currentCertificate);
            return cert is not null && cert.NotAfter > DateTime.UtcNow;
        }

        #endregion

        #region Private Methods — Event Raising

        /// <summary>
        /// Raises <see cref="ICertificateRenewalPublisher.CertificateChanged"/> with per-subscriber exception isolation.
        /// </summary>
        /// <remarks>
        /// <para><b>Exception isolation:</b> Each subscriber is invoked in its own <c>try/catch</c> block so that a
        /// faulting subscriber (e.g. <c>NntpListener</c> failing to bind the TLS socket) does not prevent
        /// subsequent subscribers from being notified.  The exception is logged at <see cref="LogLevel.Error"/> via
        /// <see cref="LogSubscriberException"/> — it does not propagate to the caller
        /// (<see cref="ActivateCertificate"/>).  This is critical because <see cref="ActivateCertificate"/> must always
        /// proceed to <see cref="CleanupCngKeyImmediately"/> and <see cref="DeferCertificateDisposal"/> after event
        /// notification — if an unhandled subscriber exception aborted the method, the superseded certificate's CNG key
        /// would never be cleaned up and the certificate would never be scheduled for disposal, causing a resource leak
        /// (the <see cref="X509Certificate2"/> and its CNG key on Windows would remain allocated until process
        /// exit).</para>
        ///
        /// <para><b>Thread safety — delegate snapshot:</b> The delegate is captured into a local variable
        /// (<c>handler</c>) before iteration via the standard C# event pattern.  This prevents a
        /// <see cref="NullReferenceException"/> if the last subscriber is removed between the null check and the
        /// <see cref="Delegate.GetInvocationList"/> call.  If a subscriber is added or removed concurrently, the change
        /// is not visible to this invocation — only to subsequent <see cref="ActivateCertificate"/> calls.  This is the
        /// standard .NET event thread-safety guarantee.</para>
        ///
        /// <para><b>Invocation list iteration:</b> <see cref="Delegate.GetInvocationList"/> returns a new
        /// <see cref="Delegate"/>[] array on every call — this is a one-time allocation per certificate activation
        /// (typically once every ~60 days during renewal, or once at startup for cached certificate load).  The
        /// alternative of invoking the multicast delegate directly (<c>handler(cert)</c>) would propagate the first
        /// subscriber's exception and skip all subsequent subscribers — unacceptable for the isolation guarantee.</para>
        ///
        /// <para><b>Current subscribers:</b></para>
        /// <list type="bullet">
        ///   <item><description><c>NntpSocketAcceptor.OnCertificateChanged</c> — atomically swaps
        ///     <c>_tlsCertificate</c> and, on first arrival, late-binds the NNTPS listening
        ///     socket.</description></item>
        ///   <item><description><c>PeerFetchListener.OnCertificateChanged</c> — atomically swaps
        ///     <c>_tlsCertificate</c> and, on first arrival, builds <see cref="System.Net.Security.SslServerAuthenticationOptions"/>
        ///     and signals the startup gate.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="cert">The newly activated certificate to broadcast to subscribers.</param>
        private void RaiseCertificateChanged(X509Certificate2 cert)
        {
            Action<X509Certificate2>? handler = _certificateChanged;
            if (handler is null)
                return;

            foreach (Delegate subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<X509Certificate2>)subscriber)(cert);
                }
                catch (Exception ex)
                {
                    LogSubscriberException(ex);
                }
            }
        }

        #endregion

        #region Private Methods — CNG Key Cleanup

        /// <summary>
        /// Immediately deletes the superseded certificate's persisted CNG private key from the Windows key store, preventing
        /// orphaned key files from accumulating in <c>%APPDATA%\Microsoft\Crypto\Keys</c> across process restarts.
        /// </summary>
        /// <remarks>
        /// <para><b>Why synchronous cleanup is needed:</b> <see cref="DeferCertificateDisposal"/> schedules certificate
        /// disposal after <see cref="OldCertificateDisposalDelay"/> (5 minutes).  If the process exits before the timer
        /// fires — which is common during development restarts, deployment rolling updates, or crash scenarios — the
        /// deferred disposal never runs, and the superseded certificate's CNG key is orphaned.  Each restart cycle that
        /// loads a PFX with <see cref="X509KeyStorageFlags.PersistKeySet"/> creates a new CNG key file, so orphaned keys
        /// accumulate indefinitely.</para>
        ///
        /// <para><b>Safe for in-flight TLS handshakes:</b> Deleting the persisted CNG key file does not invalidate the
        /// in-memory key handle.  <see cref="System.Net.Security.SslStream"/> sessions that have already completed their
        /// TLS handshake hold the private key in their SChannel security context — not via the persisted key file.  Sessions
        /// that are mid-handshake at the exact moment of key deletion are also safe: the
        /// <see cref="System.Net.Security.SslServerAuthenticationOptions.ServerCertificateSelectionCallback"/> has already
        /// returned the old certificate object to SslStream's internal state machine, which holds a reference to the
        /// in-memory key.  The CNG key store file is only needed for the initial import via
        /// <see cref="X509Certificate2(byte[], string?, X509KeyStorageFlags)"/>.</para>
        ///
        /// <para><b>Mechanism:</b> Uses the same approach as <see cref="CertificateStore.DisposeCertificate"/>: calls
        /// <see cref="ECDsaCertificateExtensions.GetECDsaPrivateKey(X509Certificate2)"/> to obtain a CNG key handle, then disposes it to trigger
        /// CNG key deletion.  <see cref="RSACertificateExtensions.GetRSAPrivateKey(X509Certificate2)"/> is called as a fallback for forward
        /// compatibility with RSA keys.</para>
        ///
        /// <para><b>Linux:</b> On Linux, <see cref="X509KeyStorageFlags.EphemeralKeySet"/> keeps the key in memory only —
        /// there is no persisted key file to clean up.  The <see cref="OperatingSystem.IsWindows"/> guard avoids
        /// unnecessary calls.</para>
        ///
        /// <para><b>Null guard:</b> Returns immediately when <paramref name="old"/> is <see langword="null"/> (first
        /// certificate activation — <see cref="TryLoadCachedCertificate"/> on startup).</para>
        ///
        /// <para><b>Best-effort:</b> Key deletion failures are caught and logged at <see cref="LogLevel.Debug"/> via
        /// <see cref="LogCngKeyCleanupFailed"/>.  Failure does not affect certificate activation or the subsequent
        /// deferred disposal.  Common failure cause: the key was already deleted by a concurrent
        /// <see cref="CertificateStore.DisposeCertificate"/> call from
        /// <c>NntpSocketAcceptor.OnCertificateChanged</c> (the known double-dispose race documented in the
        /// file-level comments).</para>
        ///
        /// <para><b>Interaction with <see cref="DeferCertificateDisposal"/>:</b> After this method deletes the CNG key,
        /// <see cref="DeferCertificateDisposal"/> still schedules the certificate for deferred disposal.  When the timer
        /// fires, <see cref="CertificateStore.DisposeCertificate"/> calls <c>GetECDsaPrivateKey()</c> on the same
        /// certificate — the CNG key file is already gone, so this throws <see cref="System.Security.Cryptography.CryptographicException"/>
        /// ("The system cannot find the file specified"), which is silently swallowed by the best-effort catch block.
        /// <see cref="IDisposable.Dispose"/> is then called to release the in-memory handle.  This double-cleanup
        /// is harmless and expected.</para>
        /// </remarks>
        /// <param name="old">The superseded certificate whose CNG key should be deleted, or <see langword="null"/> if
        /// there was no previous certificate.</param>
        private void CleanupCngKeyImmediately(X509Certificate2? old)
        {
            if (old is null || !OperatingSystem.IsWindows())
                return;

            try
            {
                using (old.GetECDsaPrivateKey()) { }
                using (old.GetRSAPrivateKey()) { }
            }
            catch (Exception ex)
            {
                LogCngKeyCleanupFailed(ex);
            }
        }

        #endregion

        #region Private Methods — Deferred Disposal

        /// <summary>
        /// Defers disposal of a superseded certificate by <see cref="OldCertificateDisposalDelay"/> (5 minutes).  Active
        /// <see cref="System.Net.Security.SslStream"/> sessions may still hold a reference to the old certificate for
        /// in-progress TLS handshakes — immediate disposal would cause those handshakes to fail with
        /// <see cref="System.Security.Authentication.AuthenticationException"/> or
        /// <see cref="System.Security.Cryptography.CryptographicException"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Why 5 minutes:</b> The longest possible TLS handshake timeout in this codebase is
        /// <c>TlsHandshakeTimeoutMs</c> (15 seconds) in <c>Vector.NNTP.Sockets.Session.NntpSession</c>.  Five minutes is 20×
        /// this value, providing generous headroom for:</para>
        /// <list type="bullet">
        ///   <item><description>Handshakes that started just before the certificate swap — the
        ///     <see cref="System.Net.Security.SslServerAuthenticationOptions.ServerCertificateSelectionCallback"/> may have
        ///     already returned the old certificate to SslStream's internal state machine before the swap
        ///     occurred.</description></item>
        ///   <item><description>Thread scheduling delays under extreme CPU load — a handshake that acquired the old
        ///     certificate reference but has not yet called into SslStream could be delayed by GC pauses or thread
        ///     starvation.</description></item>
        ///   <item><description>Consistency with <c>NntpSocketAcceptor.OnCertificateChanged</c> which uses the same
        ///     5-minute delay (<c>CertificateDisposalDelay</c>).</description></item>
        /// </list>
        ///
        /// <para><b>Delegates to <see cref="CertificateStore.DeferDisposal"/>:</b> The centralised implementation uses
        /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> followed by
        /// <see cref="Task.ContinueWith{TResult}(Func{Task, object?, TResult}, object?, CancellationToken, TaskContinuationOptions, TaskScheduler)"/>
        /// with a <see langword="static"/> lambda and state-passing to avoid a compiler-generated closure allocation.
        /// <see cref="TaskContinuationOptions.ExecuteSynchronously"/> avoids a thread-pool hop for the trivial
        /// <see cref="IDisposable.Dispose"/> call.</para>
        ///
        /// <para><b>Timer lifecycle and cancellation:</b> The delay is linked to <see cref="_stoppingToken"/> so the timer
        /// is cancelled cleanly during host shutdown.  The continuation uses <see cref="CancellationToken.None"/>
        /// to ensure it always schedules — the <c>t.Status == TaskStatus.RanToCompletion</c> guard inside the continuation
        /// prevents disposal during shutdown when <see cref="Dispose"/> may have already cleaned up
        /// <see cref="_currentCertificate"/>.</para>
        ///
        /// <para><b>CNG key state on Windows:</b> When <see cref="CleanupCngKeyImmediately"/> has already deleted the
        /// superseded certificate's CNG key before this method is called, the deferred
        /// <see cref="CertificateStore.DisposeCertificate"/> call will attempt <c>GetECDsaPrivateKey()</c> on a certificate
        /// whose persisted key is already gone — throwing <see cref="System.Security.Cryptography.CryptographicException"/>
        /// which is silently swallowed by the best-effort catch block.  <see cref="IDisposable.Dispose"/> is still
        /// called to release the in-memory handle.  This is the expected sequence.</para>
        ///
        /// <para><b>Sole ownership:</b> Only this method (and <see cref="Dispose"/> for final cleanup) may dispose
        /// certificates owned by this service.  <see cref="ICertificateRenewalPublisher.CertificateChanged"/> subscribers must not dispose the old
        /// certificate they swap out — see the ownership contract documented on <see cref="ICertificateRenewalPublisher.CertificateChanged"/> and the
        /// class-level remarks in the primary partial.</para>
        ///
        /// <para><b>Null guard:</b> <see cref="CertificateStore.DeferDisposal"/> returns immediately if
        /// <paramref name="old"/> is <see langword="null"/> — no timer is created.  This handles the first activation
        /// where there is no previous certificate to dispose.</para>
        /// </remarks>
        /// <param name="old">The superseded certificate to dispose after the delay, or <see langword="null"/> if there was
        /// no previous certificate (first activation — <see cref="TryLoadCachedCertificate"/>).</param>
        private void DeferCertificateDisposal(X509Certificate2? old)
        {
            CertificateStore.DeferDisposal(old, OldCertificateDisposalDelay, _stoppingToken);
        }

        #endregion
    }

}
