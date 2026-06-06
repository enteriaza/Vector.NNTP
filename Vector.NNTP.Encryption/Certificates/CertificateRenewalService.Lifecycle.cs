// <copyright file="CertificateRenewalService.Lifecycle.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateRenewalService.Lifecycle.cs — BackgroundService lifecycle: options validation, environment enforcement,
// ACME renewal orchestration.
//
// ExecuteAsync              — Options validation, environment enforcement, subsystem creation, startup retry loop with
//                             exponential back-off (via NetworkUtilities.CalculateBackOff), and steady-state periodic
//                             renewal via PeriodicTimer.
// CheckAndRenewAsync        — Evaluates certificate expiry and delegates to the ACME provider for renewal.
// TryLoadCachedCertificate  — Loads a cached PFX from disk for instant TLS availability without an ACME round-trip.
//
// Callers:
//   ExecuteAsync is invoked by the .NET Generic Host BackgroundService infrastructure.
//   All other methods are private, called only from ExecuteAsync or from each other.
//
// Exception safety:
//   Every catch block that swallows exceptions filters on OperationCanceledException to ensure host-shutdown
//   cancellation propagates cleanly rather than being logged as a retry-worthy failure.  Task.Delay calls pass the
//   stoppingToken so deferred waits are cancelled promptly.  CheckAndRenewAsync exceptions (ACME failure, Cloudflare
//   API error) propagate to the startup retry loop or are logged in the steady-state loop -- they do not crash the
//   service.
//
// Certificate leak prevention:
//   The renewal path in CheckAndRenewAsync wraps the certificate returned by RequestCertificateAsync in a try/finally
//   guard.  If ActivateCertificate throws (e.g. ObjectDisposedException during host shutdown), the certificate is
//   disposed via CertificateStore.DisposeCertificate to prevent leaking the X509Certificate2 and (on Windows) its
//   persisted CNG key.  After successful activation, the local variable is set to null to prevent the finally block
//   from double-disposing the certificate that is now live for TLS handshakes.
//
// SIMD applicability:
//   Not applicable.  This partial contains async orchestration — options validation, PeriodicTimer loops, Task.Delay
//   waits, and ACME provider calls.  There are no contiguous memory buffers, byte-level pattern searches, or bulk
//   numeric operations that would benefit from vector intrinsics.
//
// Cross-platform compatibility:
//   Fully compatible with Linux and Windows (ARM is not required).  No platform-specific APIs are used directly in
//   this file.  Platform-specific behaviour (CNG key cleanup on Windows, EphemeralKeySet on Linux) is handled by
//   downstream subsystems (CertificateStore, CertificateDefaults).

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Vector.NNTP.Encryption.Certificates.Acme;
using Vector.NNTP.Encryption.Cluster;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Telemetry;
using Vector.NNTP.Utilities.Retry;
using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Consuming;
using Vector.NNTP.MessageBus.Publishing;

namespace Vector.NNTP.Encryption.Certificates
{

    internal sealed partial class CertificateRenewalService
    {
        #region BackgroundService Lifecycle

        /// <summary>
        /// Main entry point invoked by the .NET Generic Host <see cref="BackgroundService"/> infrastructure.  Validates
        /// options, enforces environment-specific policies, initialises subsystems, loads any cached certificate for instant
        /// TLS availability, runs a startup retry loop with exponential back-off until the first certificate is obtained,
        /// then enters a steady-state periodic renewal loop.
        /// </summary>
        /// <remarks>
        /// <para><b>Initialisation order:</b></para>
        /// <list type="number">
        ///   <item><description>Validate <see cref="LetsEncryptOptions"/> via <see cref="IOptions{TOptions}.Value"/>.  A
        ///     failed validation triggers <see cref="IHostApplicationLifetime.StopApplication"/> — the service cannot operate
        ///     with invalid configuration.</description></item>
        ///   <item><description>If the hosting environment is Development, force
        ///     <see cref="LetsEncryptOptions.UseStagingDirectory"/> to <see langword="true"/> to prevent consuming production
        ///     rate limits.</description></item>
        ///   <item><description>Create the <see cref="CertificateStore"/> and <see cref="AcmeCertificateProvider"/>
        ///     subsystems.</description></item>
        ///   <item><description>Ensure the <c>certs/</c> directory exists with <c>0700</c> permissions on
        ///     Linux.</description></item>
        ///   <item><description>Attempt to load a cached certificate from disk for instant TLS
        ///     availability.</description></item>
        /// </list>
        ///
        /// <para><b>Startup retry loop:</b> If no certificate is present after the cached load attempt, the service enters
        /// an exponential back-off retry loop that calls <see cref="CheckAndRenewAsync"/> until a certificate is obtained.
        /// The back-off delay is computed by <see cref="RetryUtilities.CalculateBackOff(int, int, int, int)"/> with no jitter.  The attempt
        /// counter is never reset because the loop exits as soon as <see cref="IsCertificatePresent"/> returns
        /// <see langword="true"/>; if <see cref="CheckAndRenewAsync"/> succeeds, the next loop iteration's condition check
        /// terminates the loop before the counter could be used again.</para>
        ///
        /// <para><b>Steady-state renewal loop:</b> After the first certificate is obtained, a <see cref="PeriodicTimer"/>
        /// fires at <see cref="LetsEncryptOptions.RenewalCheckIntervalHours"/> intervals.
        /// <see cref="CheckAndRenewAsync"/> evaluates the certificate's expiry against the threshold and performs renewal
        /// only when needed.  Exceptions are logged and the loop continues — a single failed check does not crash the
        /// service.</para>
        ///
        /// <para><b>Cancellation:</b> Both loops propagate <see cref="OperationCanceledException"/> when
        /// <paramref name="stoppingToken"/> is cancelled, ensuring clean shutdown.  The startup retry loop uses an explicit
        /// <c>when (stoppingToken.IsCancellationRequested)</c> filter to distinguish host shutdown from ACME-internal
        /// cancellation (e.g. <see cref="HttpClient.Timeout"/> expiry inside Certes).  The steady-state loop's
        /// <see cref="PeriodicTimer.WaitForNextTickAsync"/> returns <see langword="false"/> on cancellation, terminating the
        /// loop naturally.</para>
        ///
        /// <para><b>PeriodicTimer disposal:</b> The <see cref="PeriodicTimer"/> is created with a <see langword="using"/>
        /// declaration, ensuring it is disposed when <see cref="ExecuteAsync"/> exits — whether from cancellation, an
        /// unhandled exception in the startup phase, or the steady-state <c>break</c> on cancellation.</para>
        /// </remarks>
        /// <param name="stoppingToken">Fires when the host requests a graceful shutdown.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Phase 0: Options validation.
            // Deferred from the constructor to ensure ValidateOnStart() runs first via the host's standard pipeline.
            // An invalid configuration is fatal -- the service cannot operate without domain names, Cloudflare credentials,
            // and a valid email for Let's Encrypt account registration.
            try
            {
                _options = options.Value;
            }
            catch (OptionsValidationException ex)
            {
                LogConfigurationInvalid(string.Join("; ", ex.Failures));
                hostLifetime.StopApplication();
                return;
            }

            if (!_options.Enabled)
            {
                LogAutoRenewalDisabled();
                if (!string.IsNullOrWhiteSpace(_options.CertDir))
                {
                    _store = new CertificateStore(logger, _options.CertDir, _options.PfxExportPassword);
                    TryLoadCachedCertificate();
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(_nodeName))
            {
                LogConfigurationInvalid("NntpServer:NodeName is required when Let's Encrypt is enabled.");
                hostLifetime.StopApplication();
                return;
            }

            // Phase 1: Environment enforcement.
            // Force staging directory in Development to prevent consuming production rate limits and to avoid issuing
            // untrusted certificates that could confuse debugging.  This is a safety net -- even if the operator
            // accidentally sets UseStagingDirectory=false in appsettings.Development.json, the service will use staging.
            if (hostEnvironment.IsDevelopment() && !_options.UseStagingDirectory)
            {
                _options.UseStagingDirectory = true;
                LogForcingStagingDirectory();
            }

            LogEncryptionInitialized(
                FormatDomainSummary(_options.DomainNames),
                _options.UseStagingDirectory ? "Staging" : "Production",
                _options.ClusterEnabled,
                _nodeName,
                _options.CertDir);

            // Phase 2: Subsystem creation.
            _stoppingToken = stoppingToken;
            _store = new CertificateStore(logger, _options.CertDir, _options.PfxExportPassword);
            _acmeProvider = new AcmeCertificateProvider(logger, _options, dnsTxtProbe);

            if (_options.ClusterEnabled)
            {
                IRabbitMqConnectionFactory? connectionFactory = serviceProvider.GetService<IRabbitMqConnectionFactory>();
                IRabbitMqPublisherPool? publisherPool = serviceProvider.GetService<IRabbitMqPublisherPool>();
                IRabbitMqConsumerManager? consumerManager = serviceProvider.GetService<IRabbitMqConsumerManager>();
                RabbitMQOptions? rabbitOptions = serviceProvider.GetService<IOptions<RabbitMQOptions>>()?.Value;

                if (connectionFactory is not null && publisherPool is not null && consumerManager is not null && rabbitOptions is not null)
                {
                    _clusterSync = new CertificateClusterSync(
                        logger,
                        () => _options,
                        hostEnvironment,
                        connectionFactory,
                        rabbitOptions,
                        publisherPool,
                        consumerManager,
                        _store,
                        cert =>
                        {
                            ActivateCertificate(cert);
                            return Task.CompletedTask;
                        },
                        _metrics);

                    await _clusterSync.StartAsync(stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    LogClusterSyncDisabled();
                }
            }

            _store.EnsureCertsDirectory();

            // Phase 3: Cached certificate load.
            TryLoadCachedCertificate();

            // Phase 4: Startup retry loop.
            // Exponential back-off until first certificate is obtained.  startupAttempt is never reset because the loop
            // exits as soon as IsCertificatePresent() returns true.  If CheckAndRenewAsync succeeds, the next
            // IsCertificatePresent() check terminates the loop before the counter could be used again.
            int startupAttempt = 0;

            while (!IsCertificatePresent() && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndRenewAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    using Activity? startupActivity = EncryptionTelemetry.ActivitySource.StartActivity(
                        "encryption.startup.retry",
                        ActivityKind.Internal);
                    _ = startupActivity?.SetTag("encryption.startup.attempt", startupAttempt + 1);
                    using (logger.BeginScope(new Dictionary<string, object?>
                    {
                        ["FailureReason"] = EncryptionFailureClassifier.Classify(ex),
                    }))
                    {
                        startupAttempt++;
                        int delayMs = RetryUtilities.CalculateBackOff(startupAttempt, StartupRetryBaseDelayMs, StartupRetryMaxDelayMs);

                        LogStartupRetry(ex, startupAttempt, delayMs / 1_000);

                        await Task.Delay(delayMs, stoppingToken).ConfigureAwait(false);
                    }
                }
            }

            // Phase 5: Steady-state renewal loop with jittered idle delay.
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan delay = ComputeJitteredRenewalCheckDelay(_options);
                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                using Activity? activity = EncryptionTelemetry.ActivitySource.StartActivity(
                    "encryption.renewal.check",
                    ActivityKind.Internal);
                _ = activity?.SetTag("encryption.node_name", _nodeName);
                _ = activity?.SetTag("encryption.staging", _options.UseStagingDirectory);
                _ = activity?.SetTag("encryption.domain", FormatDomainSummary(_options.DomainNames));

                try
                {
                    await CheckAndRenewAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _metrics.RecordRenewalCheck("failed");
                    using (logger.BeginScope(new Dictionary<string, object?>
                    {
                        ["FailureReason"] = EncryptionFailureClassifier.Classify(ex),
                    }))
                    {
                        LogRenewalCheckFailed(ex, _options.RenewalCheckIntervalHours);
                    }
                }
            }
        }

        /// <summary>
        /// Computes the next steady-state renewal poll delay with symmetric jitter around
        /// <see cref="LetsEncryptOptions.RenewalCheckIntervalHours"/>.
        /// </summary>
        /// <param name="options">Let's Encrypt options.</param>
        /// <returns>Jittered delay before the next renewal evaluation.</returns>
        private static TimeSpan ComputeJitteredRenewalCheckDelay(LetsEncryptOptions options)
        {
            double baseHours = options.RenewalCheckIntervalHours;
            double jitter = 1 + (Random.Shared.NextDouble() * 2 * options.RenewalJitterRatio) - options.RenewalJitterRatio;
            return TimeSpan.FromHours(baseHours * jitter);
        }

        #endregion

        #region Private Methods — Certificate Loading and Renewal

        /// <summary>
        /// Attempts to load a cached certificate from disk via <see cref="CertificateStore.TryLoadCachedCertificate"/>.
        /// If a valid unexpired certificate is found, it is activated immediately via <see cref="ActivateCertificate"/>
        /// — providing instant TLS availability without an ACME round-trip.
        /// </summary>
        /// <remarks>
        /// <para><b>First activation:</b> On first startup, <see cref="ActivateCertificate"/> will attempt to defer
        /// disposal of the superseded certificate.  Since there is no previous certificate, the
        /// <see cref="DeferCertificateDisposal"/> call is a no-op (null guard returns immediately).</para>
        ///
        /// <para><b>Exception safety:</b> <see cref="CertificateStore.TryLoadCachedCertificate"/> catches and logs all
        /// errors internally (corrupt PFX, expired certificate, I/O failure) and returns <see langword="null"/>.  If
        /// <see cref="ActivateCertificate"/> throws (e.g. <see cref="ObjectDisposedException"/> during a concurrent
        /// <see cref="Dispose"/>), the certificate is disposed via <see cref="CertificateStore.DisposeCertificate"/> in a
        /// <c>try/finally</c> guard to prevent leaking the <see cref="X509Certificate2"/> and (on Windows) its persisted
        /// CNG key.  After successful activation, the local variable is set to <see langword="null"/> to prevent the
        /// <see langword="finally"/> block from double-disposing the certificate that is now live for TLS
        /// handshakes.</para>
        /// </remarks>
        private void TryLoadCachedCertificate()
        {
            X509Certificate2? cert = _store!.TryLoadCachedCertificate();
            if (cert is null)
                return;

            try
            {
                ActivateCertificate(cert);
                cert = null; // Ownership transferred — prevent disposal in finally.
            }
            finally
            {
                // Dispose the certificate if ActivateCertificate threw (e.g. ObjectDisposedException from a concurrent
                // Dispose call).  Uses DisposeCertificate to clean up the persisted CNG key on Windows.
                if (cert is not null)
                    CertificateStore.DisposeCertificate(cert, logger);
            }
        }

        /// <summary>
        /// Evaluates the current certificate's expiry against the renewal threshold and performs an ACME renewal when
        /// needed.
        /// </summary>
        /// <remarks>
        /// <para><b>Entry gate:</b> <see cref="IsCertificateValidBeyondThreshold"/> is called first to determine whether
        /// renewal is needed.  If the certificate is valid beyond the threshold, the method returns immediately — the
        /// Debug-level log emitted by <see cref="IsCertificateValidBeyondThreshold"/> is sufficient for the happy path.
        /// When renewal <em>is</em> needed, a single <see cref="GetCurrentCertificate"/> read determines whether to log
        /// the expiring-soon message (Information, with days and threshold) or the no-certificate message (Information).
        /// This avoids duplicating the threshold arithmetic that <see cref="IsCertificateValidBeyondThreshold"/> already
        /// performs, and eliminates a second atomic read of <see cref="_currentCertificate"/> on the happy path.</para>
        ///
        /// <para><b>PFX bytes for cluster:</b> The issued PFX is already written to disk by
        /// <see cref="AcmeCertificateProvider.RequestCertificateAsync"/>.  Cluster broadcast reads those bytes via
        /// <see cref="CertificateStore.TryLoadCertificateBytesAsync"/> instead of re-exporting from
        /// <see cref="X509Certificate2"/> (Windows CNG keys loaded without <see cref="X509KeyStorageFlags.Exportable"/>
        /// cannot be exported again).</para>
        ///
        /// <para><b>Certificate leak prevention:</b> The certificate returned by
        /// <see cref="AcmeCertificateProvider.RequestCertificateAsync"/> is wrapped in a <c>try/finally</c> guard.  If
        /// <see cref="ActivateCertificate"/> throws (e.g. <see cref="ObjectDisposedException"/> during host shutdown, or
        /// any future exception from pre-activation validation), the certificate is disposed via
        /// <see cref="CertificateStore.DisposeCertificate"/> to clean up the <see cref="X509Certificate2"/> and (on
        /// Windows) its persisted CNG key.  After successful activation, the local variable is set to
        /// <see langword="null"/> to prevent the <see langword="finally"/> block from double-disposing the certificate
        /// that is now live for TLS handshakes.  The subsequent <see cref="LogNewCertificateActive"/> call reads state
        /// from the activated certificate via <see cref="GetCurrentCertificate"/> — safe because the certificate is not
        /// disposed until superseded by a future activation or during <see cref="Dispose"/>.</para>
        ///
        /// <para><b>Exception propagation:</b> Exceptions from <see cref="AcmeCertificateProvider.RequestCertificateAsync"/>
        /// (ACME failure, Cloudflare API error, <see cref="TimeoutException"/>) propagate to the caller (the startup retry
        /// loop or the steady-state renewal loop), which handles logging and retry.
        /// <see cref="OperationCanceledException"/> is not caught here — it propagates to the caller's filter, which
        /// distinguishes host shutdown from internal cancellation.</para>
        /// </remarks>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        private async Task CheckAndRenewAsync(CancellationToken ct)
        {
            // Delegate the threshold check to the shared helper -- avoids duplicating the expiry arithmetic and the
            // atomic read of _currentCertificate.  On the happy path (certificate still valid), this is the only read.
            if (IsCertificateValidBeyondThreshold())
            {
                _metrics.RecordRenewalCheck("skipped");
                return;
            }

            if (_options.ClusterEnabled && _clusterSync is not null)
            {
                await _clusterSync.TryRenewAsLeaderAsync(PerformRenewalAsync, ct).ConfigureAwait(false);
                return;
            }

            await PerformRenewalAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Performs ACME renewal when the certificate is absent or within the renewal threshold.
        /// </summary>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        private async Task PerformRenewalAsync(CancellationToken ct)
        {
            _renewalCorrelationId = Guid.NewGuid().ToString("N");
            Stopwatch issueStopwatch = Stopwatch.StartNew();

            using Activity? activity = EncryptionTelemetry.ActivitySource.StartActivity(
                "encryption.certificate.issue",
                ActivityKind.Client);
            _ = activity?.SetTag("encryption.renewal_id", _renewalCorrelationId);
            _ = activity?.SetTag("encryption.node_name", _nodeName);
            _ = activity?.SetTag("encryption.staging", _options.UseStagingDirectory);
            _ = activity?.SetTag("encryption.domain", FormatDomainSummary(_options.DomainNames));

            // Renewal is needed.  Read the certificate once to determine the appropriate Information-level log message.
            X509Certificate2? current = GetCurrentCertificate();

            if (current is not null)
            {
                TimeSpan remaining = current.NotAfter - DateTime.UtcNow;
                LogCertificateExpiringSoon(remaining.TotalDays, _options.RenewBeforeExpiryDays);
            }
            else
            {
                LogNoCertificate();
            }

            using (logger.BeginScope(new Dictionary<string, object?> { ["RenewalId"] = _renewalCorrelationId }))
            {
                try
                {
                    X509Certificate2? newCert = await _acmeProvider!.RequestCertificateAsync(_store!, ct).ConfigureAwait(false);

                    try
                    {
                        ActivateCertificate(newCert);
                        newCert = null;

                        if (_clusterSync is not null)
                        {
                            byte[]? pfxBytes = await _store!.TryLoadCertificateBytesAsync(ct).ConfigureAwait(false);
                            if (pfxBytes is null || pfxBytes.Length == 0)
                            {
                                throw new InvalidOperationException(
                                    "Cluster broadcast requires certificate.pfx on disk after renewal, but the file could not be read.");
                            }

                            X509Certificate2? activated = GetCurrentCertificate();
                            if (activated is not null)
                            {
                                await _clusterSync.PublishAndRecordAsync(activated, pfxBytes, ct, _renewalCorrelationId).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        if (newCert is not null)
                        {
                            CertificateStore.DisposeCertificate(newCert, logger);
                        }
                    }

                    X509Certificate2? activatedCert = GetCurrentCertificate();
                    if (activatedCert is not null)
                    {
                        LogNewCertificateActive(activatedCert.Subject, activatedCert.Thumbprint, activatedCert.NotAfter);
                    }

                    _metrics.RecordCertificateIssue("success");
                    _metrics.RecordRenewalCheck("renewed");
                }
                catch (OperationCanceledException)
                {
                    _metrics.RecordCertificateIssue("cancelled");
                    throw;
                }
                catch (Exception)
                {
                    _metrics.RecordCertificateIssue("transient_failure");
                    throw;
                }
                finally
                {
                    issueStopwatch.Stop();
                    _metrics.RecordCertificateIssueDuration(issueStopwatch.Elapsed.TotalMilliseconds);
                    _renewalCorrelationId = null;
                }
            }
        }

        /// <summary>
        /// Formats configured domain names for structured logging without exposing secrets.
        /// </summary>
        /// <param name="domainNames">Configured ACME domain names.</param>
        /// <returns>Comma-separated domain summary or empty when unset.</returns>
        private static string FormatDomainSummary(string[] domainNames)
        {
            return domainNames.Length == 0 ? string.Empty : string.Join(',', domainNames);
        }

        #endregion
    }

}
