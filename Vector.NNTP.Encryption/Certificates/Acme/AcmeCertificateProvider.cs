// <copyright file="AcmeCertificateProvider.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AcmeCertificateProvider.cs — RequestCertificateAsync entry point, and IDisposable implementation.
//
// This class encapsulates all ACME protocol interactions with Let's Encrypt: account management, DNS-01 challenge
// orchestration via Cloudflare, authoritative DNS polling, and order finalisation.  The implementation is split across
// companion partial files, each encapsulating a single responsibility:
//
//   AcmeCertificateProvider.AcmeAccount.cs            — Account key loading from configuration and ACME context
//                                                        creation.
//   AcmeCertificateProvider.ChallengeOrchestration.cs — DNS-01 challenge creation, authoritative DNS polling,
//                                                        and challenge validation with Let's Encrypt.
//   AcmeCertificateProvider.OrderFinalisation.cs      — Order readiness polling, CSR generation, PFX construction,
//                                                        and certificate key management.
//   AcmeCertificateProvider.CloudflareDns.cs          — Cloudflare REST API interactions: TXT record CRUD,
//                                                        authoritative nameserver resolution, and DNS propagation
//                                                        polling.
//   AcmeCertificateProvider.Logging.cs                — [LoggerMessage] source-generated partial methods for all
//                                                        structured log messages across all partial files.
//
// Lifecycle:
//   Created by CertificateRenewalService.ExecuteAsync once options are validated and LetsEncryptOptions.Enabled is
//   true.  The only mutable instance state is the cached authoritative nameserver IPs (resolved once on first renewal,
//   reused across subsequent cycles to avoid a redundant Cloudflare GET /zones/{id} API call per renewal).
//
// Flow per call to RequestCertificateAsync:
//   1. Load ACME account key from configuration (AccountKeyPem in appsettings.json)
//   2. Resolve zone authoritative NS via Cloudflare GET /zones/{id} (cached after first call)
//   3. Create ACME order for all DomainNames
//   4. For each authorization:
//      a. Create _acme-challenge TXT record via Cloudflare API
//      b. Poll authoritative NS until TXT visible (~2-5 s)
//      c. Validate challenge with Let's Encrypt
//   5. Finalise order with ES256 CSR -> PFX -> disk
//   6. Cleanup all TXT records (best-effort, uncancellable)
//
// Security:
//   The static CloudflareHttpClient does NOT carry a default Authorization header.  Tokens are set per-request
//   and unconditionally scrubbed in finally blocks by each Cloudflare API method in CloudflareDns.cs.  This
//   prevents token leakage via exception propagation, debugger inspection, or memory dumps of the HttpClient.
//
// Cross-platform:
//   Fully portable.  All methods use BCL APIs available on all .NET 8 runtimes (Windows x64, Linux x64).  No
//   P/Invoke, no OS-specific APIs.  PFX key storage flags are resolved at type-load time by
//   CertificateDefaults.PfxKeyStorageFlags via OperatingSystem.IsWindows().
//
// SIMD applicability:
//   Not applicable.  This class orchestrates HTTP API calls, JSON parsing, and certificate operations.  There
//   are no contiguous memory buffers, byte-level pattern searches, or bulk numeric operations that would
//   benefit from vector instructions.
//
// Callers:
//   CertificateRenewalService.CheckAndRenewAsync -- sole consumer via RequestCertificateAsync.

using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Vector.NNTP.Encryption.Acme;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Dns;
using Vector.NNTP.Utilities.Disposal;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Vector.NNTP.Encryption.Certificates.Acme
{

    /// <summary>
    /// Encapsulates all ACME protocol interactions with Let's Encrypt: account management, DNS-01 challenge orchestration
    /// via Cloudflare, authoritative DNS polling, and order finalisation.
    /// </summary>
    /// <remarks>
    /// <para>This class is split across multiple partial files for maintainability:</para>
    /// <list type="bullet">
    ///   <item><description><c>AcmeCertificateProvider.cs</c> (this file) -- constants, shared HTTP client, and the
    ///     top-level <see cref="RequestCertificateAsync"/> entry point.</description></item>
    ///   <item><description><c>AcmeCertificateProvider.AcmeAccount.cs</c> -- ACME account key loading from configuration
    ///     and context creation via <see cref="LoadOrCreateAccountAsync"/>.</description></item>
    ///   <item><description><c>AcmeCertificateProvider.ChallengeOrchestration.cs</c> -- DNS-01 challenge creation,
    ///     validation polling, and authoritative DNS propagation waiting.</description></item>
    ///   <item><description><c>AcmeCertificateProvider.OrderFinalisation.cs</c> -- order readiness polling, CSR generation,
    ///     PFX construction from the ACME certificate chain, and certificate key management.</description></item>
    ///   <item><description><c>AcmeCertificateProvider.CloudflareDns.cs</c> -- Cloudflare REST API interactions for TXT
    ///     record CRUD, authoritative nameserver resolution, and DNS propagation polling.</description></item>
    ///   <item><description><c>AcmeCertificateProvider.Logging.cs</c> -- <see cref="LoggerMessageAttribute"/>
    ///     source-generated partial methods for all structured log messages across all partial files.</description></item>
    /// </list>
    ///
    /// <para><b>Lifecycle:</b> Created by <see cref="CertificateRenewalService.ExecuteAsync"/> once options are validated
    /// and <see cref="LetsEncryptOptions.Enabled"/> is <see langword="true"/>.  Stateless between calls to
    /// <see cref="RequestCertificateAsync"/> -- no mutable instance state is carried across renewal cycles except the
    /// cached authoritative DNS client.</para>
    ///
    /// <para><b>ACME flow (DNS-01):</b></para>
    /// <list type="number">
    ///   <item><description>Load the ACME account key from <see cref="LetsEncryptOptions.AccountKeyPem"/> (configuration)
    ///     and verify or register the account with the ACME server.</description></item>
    ///   <item><description>Resolve the zone's authoritative nameservers via the Cloudflare <c>GET /zones/{id}</c> API,
    ///     then resolve each NS hostname to IP addresses for direct polling.</description></item>
    ///   <item><description>Create a new ACME order for all <see cref="LetsEncryptOptions.DomainNames"/>.</description></item>
    ///   <item><description>For each authorisation, create a <c>_acme-challenge</c> TXT record via the Cloudflare API,
    ///     poll the authoritative nameservers until the record is visible (typically 2--5 s), then tell Let's Encrypt to
    ///     validate.</description></item>
    ///   <item><description>Finalise the order with a CSR signed by a persisted ES256 certificate key (stable fingerprint
    ///     across renewals).</description></item>
    ///   <item><description>Save the PFX to disk via <see cref="CertificateStore"/>.</description></item>
    ///   <item><description>Clean up all TXT records (best-effort, uncancellable -- see
    ///     <see cref="CleanupTxtRecordsAsync"/>).</description></item>
    /// </list>
    ///
    /// <para><b>Authoritative DNS polling:</b> After creating a TXT record, the service polls the zone's authoritative
    /// nameservers directly (bypassing recursive resolvers and their caches) using <see cref="LegacyAuthoritativeDnsClient"/>
    /// -- a minimal UDP DNS client that replaces the DnsClient NuGet package.  This reduces DNS propagation wait time from
    /// a fixed 20--60 s to typically 2--5 s.  If authoritative nameservers cannot be resolved, a fixed
    /// <see cref="DnsFallbackDelaySeconds"/> delay is used as a safe fallback.</para>
    ///
    /// <para><b>Cloudflare API:</b> All HTTP calls use a shared static <see cref="CloudflareHttpClient"/> with
    /// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/> set to 5 minutes for DNS rotation.  The API token requires
    /// only <c>Zone:DNS:Edit</c> permission scoped to the target zone.</para>
    ///
    /// <para><b>Cloudflare error handling:</b> All API responses are validated at two levels: first the HTTP status code
    /// via <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>, then the Cloudflare envelope's <c>success</c> field
    /// via <see cref="EnsureCloudflareSuccess"/>.  Cloudflare can return HTTP 200 with <c>"success": false</c> for logical
    /// errors (invalid zone ID, permission denied, rate limits), so both checks are required.</para>
    ///
    /// <para><b>Cloudflare credential safety:</b> The static <see cref="CloudflareHttpClient"/> does not carry a default
    /// <c>Authorization</c> header.  Each Cloudflare API method sets the bearer token per-request and unconditionally
    /// clears it in a <see langword="finally"/> block before any exception propagates.  This prevents the token from
    /// leaking via exception objects, debugger inspection, or memory dumps of the long-lived
    /// <see cref="HttpClient"/>.</para>
    ///
    /// <para><b>Thread safety:</b> The class is safe for sequential reuse across renewal cycles.  It is not designed for
    /// concurrent calls to <see cref="RequestCertificateAsync"/> -- the ACME protocol is inherently sequential per
    /// order.</para>
    ///
    /// <para><b>Disposable resources:</b> The <see cref="_dnsInitLock"/> <see cref="SemaphoreSlim"/> is the sole owned
    /// disposable resource.  <see cref="Dispose"/> releases it deterministically.  The static
    /// <see cref="CloudflareHttpClient"/> is intentionally <em>not</em> disposed -- it is a process-lifetime singleton
    /// managed by <see cref="SocketsHttpHandler.PooledConnectionLifetime"/>.  <see cref="CertificateRenewalService.Dispose"/>
    /// calls <see cref="Dispose"/> via <see cref="DisposalUtilities.TryDispose"/>.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  All methods use BCL APIs available on all .NET 8 runtimes (Windows
    /// x64, Linux x64).  No P/Invoke, no OS-specific APIs.  PFX key storage flags are resolved at type-load time by
    /// <see cref="CertificateDefaults.PfxKeyStorageFlags"/> via <see cref="OperatingSystem.IsWindows"/>.</para>
    ///
    /// <para><b>SIMD applicability:</b> Not applicable.  This class orchestrates HTTP API calls, JSON parsing, and
    /// certificate operations.  There are no contiguous memory buffers, byte-level pattern searches, or bulk numeric
    /// operations that would benefit from vector instructions.</para>
    ///
    /// <para><b>Primary constructor parameters:</b></para>
    /// <list type="bullet">
    ///   <item><description><c>logger</c> -- <see cref="ILogger"/> scoped to <see cref="CertificateRenewalService"/> for
    ///     consistent log context across the certificate subsystem.  All three subsystem classes
    ///     (<see cref="CertificateStore"/>, <c>AcmeCertificateProvider</c>, <c>CertificateClusterSync</c>) share
    ///     the same logger instance so certificate-related logs appear under a single source context.</description></item>
    ///   <item><description><c>options</c> -- validated <see cref="LetsEncryptOptions"/> snapshot providing domain names,
    ///     email, Cloudflare credentials, account key PEM, and staging/production directory selection.</description></item>
    /// </list>
    /// </remarks>
    internal sealed partial class AcmeCertificateProvider(
        ILogger logger,
        LetsEncryptOptions options,
        IDnsTxtPropagationProbe dnsTxtProbe) : IDisposable
    {
        #region Constants

        /// <summary>
        /// Maximum number of times to poll Let's Encrypt for challenge validation status before raising a
        /// <see cref="TimeoutException"/>.
        /// </summary>
        /// <remarks>
        /// At <see cref="ChallengeValidationPollInterval"/> (2 s) per attempt, this gives a total timeout of 60 s -- well
        /// beyond the typical 5--15 s validation time.
        /// </remarks>
        private const int ChallengeValidationMaxAttempts = 30;

        /// <summary>
        /// TTL (in seconds) applied to <c>_acme-challenge</c> TXT records created via the Cloudflare API.
        /// </summary>
        /// <remarks>
        /// <para>A low TTL ensures stale records expire quickly from recursive resolver caches if cleanup fails.
        /// 120 seconds is the conventional ACME challenge TTL -- low enough for fast expiry, but safely above
        /// Cloudflare's minimum of 60 seconds.  Setting TTL to exactly 60 risks rejection if Cloudflare raises
        /// its floor for specific plan tiers or zone configurations.</para>
        /// <para>Cloudflare also accepts <c>ttl = 1</c> as a special value meaning "automatic" (typically 300 s),
        /// but an explicit low value is preferred so the TTL is deterministic and records don't linger for 5 minutes
        /// if the DELETE cleanup fails.</para>
        /// </remarks>
        private const int TxtRecordTtlSeconds = 120;

        /// <summary>
        /// Maximum number of concurrent Cloudflare API calls during TXT record cleanup.
        /// </summary>
        /// <remarks>
        /// Each SAN in the ACME order produces one <c>_acme-challenge</c> TXT record that must be deleted after
        /// validation.  Cloudflare's API rate limit is 1,200 requests per 5 minutes per zone, but burst behaviour on
        /// concurrent calls from a single IP is less predictable -- particularly on lower-tier plans.  Bounding
        /// concurrency to 5 prevents a large multi-SAN order (e.g. 20+ domains) from firing all DELETE requests
        /// simultaneously, while still reducing total cleanup time from <c>N x roundtrip</c> to roughly
        /// <c>ceil(N/5) x roundtrip</c>.
        /// </remarks>
        private const int CloudflareMaxConcurrentDeletes = 5;

        /// <summary>
        /// Fixed delay (in seconds) used as a DNS propagation fallback when authoritative nameservers cannot be resolved.
        /// </summary>
        /// <remarks>
        /// 20 seconds is a conservative lower bound for global DNS propagation -- sufficient for Cloudflare's typical
        /// sub-5 s propagation while accounting for slower anycast regions.
        /// </remarks>
        private const int DnsFallbackDelaySeconds = 20;

        /// <summary>
        /// Maximum number of times to poll the ACME order for <see cref="OrderStatus.Ready"/> or
        /// <see cref="OrderStatus.Valid"/> status before raising an <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <remarks>
        /// At <see cref="OrderPollInterval"/> (2 s) per attempt, this gives a total timeout of 30 s.  After all DNS-01
        /// challenges are individually validated, the server typically transitions the order to <c>ready</c> within
        /// 1--5 seconds.  30 seconds provides ample margin for high-load Let's Encrypt infrastructure.
        /// </remarks>
        private const int OrderPollMaxAttempts = 15;

        /// <summary>Interval between Let's Encrypt challenge status polls.</summary>
        private static readonly TimeSpan ChallengeValidationPollInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Interval between ACME order status polls while waiting for the order to transition to
        /// <see cref="OrderStatus.Ready"/> or <see cref="OrderStatus.Valid"/> after challenge validation or CSR
        /// submission.
        /// </summary>
        private static readonly TimeSpan OrderPollInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Maximum time to wait for a TXT record to become visible on the authoritative nameservers before proceeding
        /// with ACME validation anyway (legacy single-NS polling path).
        /// </summary>
        private static readonly TimeSpan DnsPropagationTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Interval between authoritative DNS TXT record visibility polls (legacy path).</summary>
        private static readonly TimeSpan DnsPollInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Shared <see cref="HttpClient"/> for ACME directory HEAD requests (clock-skew guard).
        /// </summary>
        private static readonly HttpClient AcmeDirectoryHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// Shared <see cref="HttpClient"/> for all Cloudflare API calls.  A single static instance is safe because
        /// <see cref="HttpClient"/> is designed for concurrent use and reuses the underlying
        /// <see cref="SocketsHttpHandler"/> connection pool.
        /// </summary>
        /// <remarks>
        /// <para><see cref="SocketsHttpHandler.PooledConnectionLifetime"/> is set to 5 minutes so the handler periodically
        /// creates new connections, picking up DNS changes for the Cloudflare API endpoint (important in multi-region
        /// anycast environments).</para>
        /// <para>Gzip, deflate, and Brotli decompression are enabled to reduce bandwidth for JSON responses.  Brotli
        /// provides ~15--20% better compression than gzip for JSON payloads and is supported by the Cloudflare API.</para>
        /// <para><b>Credential safety:</b> No default <c>Authorization</c> header is set on this client.  Each API method
        /// in <c>AcmeCertificateProvider.CloudflareDns.cs</c> sets the bearer token per-request on the
        /// <see cref="HttpRequestMessage"/> and unconditionally clears it in a <see langword="finally"/> block.  This
        /// ensures the token is never held by the long-lived static <see cref="HttpClient"/> instance.</para>
        /// <para><b>Not disposed:</b> Static <see cref="HttpClient"/> instances should not be disposed -- the
        /// <see cref="SocketsHttpHandler"/> manages connection lifetime internally via
        /// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/>.  Disposing the client would close all pooled
        /// connections and prevent subsequent requests.  This follows the
        /// <see href="https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines">Microsoft
        /// HttpClient guidelines</see> for static/singleton usage.</para>
        /// </remarks>
        private static readonly HttpClient CloudflareHttpClient = new(
            new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            })
        {
            BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        #endregion

        #region Internal Methods

        /// <summary>
        /// Performs a full ACME DNS-01 certificate request: loads the account key from configuration, resolves authoritative
        /// nameservers, creates an order, validates each domain via Cloudflare TXT records, finalises the order, and
        /// persists the certificate to disk.
        /// </summary>
        /// <remarks>
        /// <para><b>Cancellation coverage:</b> Several Certes library methods (<c>NewOrder</c>, <c>Authorizations</c>)
        /// do not accept a <see cref="CancellationToken"/>.  Explicit
        /// <see cref="CancellationToken.ThrowIfCancellationRequested"/> calls before each ensure a host shutdown that
        /// occurs during the preceding <see langword="await"/> propagates promptly rather than initiating a new outbound
        /// ACME request.  If Certes' underlying <see cref="HttpClient"/> hangs, the default 100-second
        /// <see cref="HttpClient.Timeout"/> will surface as an exception in the caller's retry loop.  The same pattern
        /// is applied in <see cref="LoadOrCreateAccountAsync"/>, <see cref="CreateCloudflareTxtRecordAsync"/>, and
        /// <see cref="ValidateChallengeAsync"/>.</para>
        ///
        /// <para><b>TXT record cleanup:</b> All <c>_acme-challenge</c> TXT records created during the flow are cleaned up
        /// in a <see langword="finally"/> block regardless of success or failure.  Cleanup uses
        /// <see cref="CancellationToken.None"/> rather than <paramref name="ct"/> -- if the host is shutting down
        /// mid-renewal, orphaning TXT records would pollute the DNS zone and may interfere with future renewals if
        /// Let's Encrypt encounters stale challenge responses.  The low <see cref="TxtRecordTtlSeconds"/> TTL (120 s)
        /// provides a secondary safety net -- stale records auto-expire in 2 minutes even if the DELETE cleanup
        /// fails.</para>
        ///
        /// <para><b>Exception propagation:</b> Exceptions from <see cref="FinaliseOrderAsync"/> (order polling timeout,
        /// invalid order status, PFX construction failure), <see cref="CreateCloudflareTxtRecordAsync"/> (Cloudflare API
        /// failure), and <see cref="ValidateChallengeAsync"/> (challenge rejection) propagate to the caller after TXT record cleanup completes in the
        /// <see langword="finally"/> block.  The caller (<see cref="CertificateRenewalService.CheckAndRenewAsync"/>)
        /// catches and retries with exponential back-off.</para>
        /// </remarks>
        /// <param name="store">Filesystem persistence for account keys, certificate keys, and the final PFX.</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>The newly provisioned <see cref="X509Certificate2"/> imported with
        /// <see cref="CertificateDefaults.PfxKeyStorageFlags"/>.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host shutdown)
        /// -- either from the explicit <see cref="CancellationToken.ThrowIfCancellationRequested"/> guards before Certes
        /// calls, or from cancellation-aware downstream methods (<see cref="LoadOrCreateAccountAsync"/>,
        /// <see cref="IDnsTxtPropagationProbe.WaitForTxtRecordsAsync"/>, <see cref="CreateCloudflareTxtRecordAsync"/>,
        /// <see cref="ValidateChallengeAsync"/>, <see cref="FinaliseOrderAsync"/>).</exception>
        /// <exception cref="AcmeRequestException">Thrown when Let's Encrypt rejects the ACME order creation (e.g.
        /// invalid domain, rate limit exceeded).  Propagates from <c>acme.NewOrder()</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a DNS-01 challenge is rejected by Let's Encrypt
        /// (<see cref="ChallengeStatus.Invalid"/>), the Cloudflare API returns a logical failure
        /// (<c>"success": false</c>), the <see cref="LetsEncryptOptions.AccountKeyPem"/> is not configured, or the
        /// ACME order does not reach the expected status within the polling budget.</exception>
        /// <exception cref="TimeoutException">Thrown when a DNS-01 challenge does not complete within
        /// <see cref="ChallengeValidationMaxAttempts"/> x <see cref="ChallengeValidationPollInterval"/>.</exception>
        internal async Task<X509Certificate2> RequestCertificateAsync(CertificateStore store, CancellationToken ct)
        {
            string[] orderDomains = CertificateOrderDomainBuilder.BuildOrderDomains(options);
            Uri directoryUri = options.UseStagingDirectory ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2;

            LogStartingCertificateRequest(orderDomains.Length, options.UseStagingDirectory ? "staging" : "production");

            await AssertClockSkewIfNeededAsync(directoryUri, ct).ConfigureAwait(false);

            AcmeContext acme = await LoadOrCreateAccountAsync(store, ct).ConfigureAwait(false);

            // Certes' NewOrder() and Authorizations() do not accept a CancellationToken -- check before each outbound ACME request.
            ct.ThrowIfCancellationRequested();
            IOrderContext order = await AcmeTransientRetry.ExecuteAsync(
                () => acme.NewOrder(orderDomains),
                logger,
                "Acme.NewOrder",
                options.AcmeTransientRetryMaxAttempts,
                ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            IEnumerable<IAuthorizationContext> authorizations = await order.Authorizations().ConfigureAwait(false);

            List<(string recordName, string recordId)> createdRecords = [];
            List<PendingDns01Challenge> pending = [];

            try
            {
                foreach (IAuthorizationContext authz in authorizations)
                {
                    ct.ThrowIfCancellationRequested();
                    IChallengeContext dns01 = await authz.Dns().ConfigureAwait(false);
                    string dnsTxt = acme.AccountKey.DnsTxt(dns01.Token);
                    Certes.Acme.Resource.Authorization authzResource = await authz.Resource().ConfigureAwait(false);
                    string domain = authzResource.Identifier?.Value
                        ?? throw new InvalidOperationException("Authorization identifier missing from ACME response.");
                    string recordName = $"_acme-challenge.{domain}";
                    pending.Add(new PendingDns01Challenge(dns01, domain, recordName, dnsTxt));
                }

                foreach (PendingDns01Challenge item in pending)
                {
                    LogSettingDnsTxtRecord(item.RecordName, item.DnsTxt);
                    string recordId = await CreateCloudflareTxtRecordAsync(item.RecordName, item.DnsTxt, ct).ConfigureAwait(false);
                    createdRecords.Add((item.RecordName, recordId));
                }

                IReadOnlyList<(string RecordName, string ExpectedTxt)> probeRecords =
                    pending.ConvertAll(static p => (p.RecordName, p.DnsTxt));
                await dnsTxtProbe.WaitForTxtRecordsAsync(probeRecords, options, ct).ConfigureAwait(false);

                foreach (PendingDns01Challenge item in pending)
                {
                    await ValidateChallengeAsync(item.Dns01, item.Domain, ct).ConfigureAwait(false);
                    LogChallengeValidated(item.Domain);
                }

                return await FinaliseOrderAsync(order, orderDomains, store, ct).ConfigureAwait(false);
            }
            finally
            {
                // Use CancellationToken.None -- orphaned TXT records pollute the DNS zone; cleanup must complete even during host shutdown.
                await CleanupTxtRecordsAsync(createdRecords, CancellationToken.None).ConfigureAwait(false);
            }
        }

        #endregion

        #region Dispose

        /// <summary>
        /// Releases the <see cref="_dnsInitLock"/> <see cref="SemaphoreSlim"/> used to guard one-time authoritative DNS
        /// resolution.
        /// </summary>
        /// <remarks>
        /// <para><b>Owned resources:</b> The only instance-owned disposable is <see cref="_dnsInitLock"/>.  The static
        /// <see cref="CloudflareHttpClient"/> is a process-lifetime singleton and is intentionally not disposed -- see its
        /// field remarks.</para>
        ///
        /// <para><b>Caller:</b> <see cref="CertificateRenewalService.Dispose"/> via
        /// <see cref="DisposalUtilities.TryDispose"/> -- invoked during host shutdown after all hosted services have
        /// stopped.  At that point, no concurrent <see cref="RequestCertificateAsync"/> call is in flight, so disposing
        /// the semaphore is safe.</para>
        /// </remarks>
        public void Dispose()
        {
            _dnsInitLock.Dispose();
        }

        #endregion
    }

}
