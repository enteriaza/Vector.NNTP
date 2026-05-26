// <copyright file="AcmeCertificateProvider.CloudflareDns.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AcmeCertificateProvider.CloudflareDns.cs — Cloudflare REST API interactions: TXT record
// creation and deletion, authoritative nameserver resolution, and DNS propagation polling.
//
// All Cloudflare API calls use the shared static CloudflareHttpClient (defined in the primary partial).  Responses are
// validated at two levels: HTTP status code, then the Cloudflare envelope's "success" field -- Cloudflare can return
// HTTP 200 with "success": false for logical errors.
//
// Responsibilities:
//   CreateAuthoritativeDnsClientAsync   -- Cached factory for the authoritative DNS client (one Cloudflare API call
//                                          per process lifetime).
//   ResolveAuthoritativeNameserversAsync -- GET /zones/{id} -> NS hostnames -> IP resolution.
//   WaitForTxtRecordAsync               -- Poll authoritative NS until a TXT record is visible or timeout expires.
//   CreateCloudflareTxtRecordAsync      -- POST /dns_records (TXT record for _acme-challenge).
//   DeleteCloudflareTxtRecordAsync      -- DELETE /dns_records/{id} (single record cleanup).
//   SendCloudflareRequestAsync          -- Shared helper encapsulating credential injection, response size validation,
//                                          envelope validation, and credential scrubbing for all Cloudflare API calls.
//   EnsureCloudflareSuccess             -- Validate the Cloudflare JSON envelope's "success" field.
//   CleanupTxtRecordsAsync              -- Bounded-concurrency best-effort deletion of all TXT records created during
//                                          a renewal cycle.
//   FormatNameserverIps                 -- Formats an IPAddress[] into a comma-delimited diagnostic string.
//
// DNS nameserver resolution:
//   ResolveAuthoritativeNameserversAsync resolves each NS hostname to IP addresses, preferring IPv4 to avoid
//   broken-IPv6 timeouts in dual-stack environments.  Results are de-duplicated by IP address to avoid redundant
//   queries to the same anycast endpoint.
//
// Concurrency:
//   CleanupTxtRecordsAsync uses Parallel.ForEachAsync with bounded concurrency (CloudflareMaxConcurrentDeletes)
//   to delete TXT records in parallel while staying within Cloudflare's rate limits.
//
//   The authoritative DNS client cache uses a SemaphoreSlim with double-check locking to guarantee exactly one
//   Cloudflare API call for nameserver resolution, even under hypothetical concurrent access.  The fast path
//   (Volatile.Read of the resolved flag) is lock-free for all calls after the first successful resolution.
//
// Security:
//   The bearer token is set per-request on the HttpRequestMessage and unconditionally scrubbed in a finally block
//   by SendCloudflareRequestAsync.  No method logs credentials (API tokens, zone IDs, PEM key content).
//   Response size is enforced at two levels: upfront Content-Length check (EnsureResponseSizeWithinLimit) and
//   streaming byte limit (LengthLimitedReadStream).
//
// Cross-platform:
//   Fully portable.  All methods use BCL APIs available on all .NET 8 runtimes (Windows x64, Linux x64).  No
//   P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.
//
// SIMD applicability:
//   Not applicable.  This file performs HTTP API calls, JSON parsing, DNS hostname resolution, and string
//   formatting.  There are no contiguous memory buffers, byte-level pattern searches, or bulk numeric operations
//   that would benefit from vector instructions.
//
// Callers (all within other AcmeCertificateProvider partials):
//   RequestCertificateAsync  -> CreateAuthoritativeDnsClientAsync, CleanupTxtRecordsAsync
//   ProcessDns01ChallengeAsync -> CreateCloudflareTxtRecordAsync, WaitForTxtRecordAsync

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Vector.NNTP.Encryption.Dns;
using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Encryption.Certificates.Acme
{

    internal sealed partial class AcmeCertificateProvider
    {
        #region Constants -- Cloudflare Response Validation

        /// <summary>
        /// Maximum number of characters allowed in the aggregated Cloudflare error detail string.  Prevents unbounded
        /// <see cref="StringBuilder"/> growth from a malformed or adversarial API response containing extremely large error
        /// arrays.
        /// </summary>
        /// <remarks>
        /// 2,048 characters is generous for Cloudflare's typical 1--3 error messages (each ~50--100 characters) while
        /// providing a hard safety cap.  Truncated output includes a <c>[truncated]</c> suffix to signal incomplete data
        /// in diagnostics.
        /// </remarks>
        private const int MaxCloudflareErrorDetailLength = 2_048;

        /// <summary>
        /// Maximum number of bytes that may be read from a Cloudflare API response body before
        /// <see cref="LengthLimitedReadStream"/> throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <remarks>
        /// <para>Cloudflare zone API responses are typically 2--5 KB; TXT record CRUD responses are ~500 bytes.  1 MB
        /// provides an extremely generous margin (200x the expected maximum) while preventing memory exhaustion from a
        /// compromised or MITM'd endpoint.</para>
        /// <para>This limit is enforced at two levels:</para>
        /// <list type="number">
        ///   <item><description><b>Upfront:</b> <see cref="EnsureResponseSizeWithinLimit"/> rejects responses whose
        ///     <c>Content-Length</c> header exceeds this value -- no body bytes are read.</description></item>
        ///   <item><description><b>Streaming:</b> <see cref="LengthLimitedReadStream"/> wraps the response stream and
        ///     throws if cumulative bytes read exceed this value -- covers chunked transfer-encoded responses that lack a
        ///     <c>Content-Length</c> header.</description></item>
        /// </list>
        /// </remarks>
        private const long MaxCloudflareResponseBytes = 1_048_576;

        #endregion

        #region Fields -- Authoritative DNS Cache

        /// <summary>
        /// Cached <see cref="AuthoritativeDnsClient"/> configured to query the zone's authoritative nameservers directly,
        /// resolved once on first renewal via <see cref="ResolveAuthoritativeNameserversAsync"/> and reused across all
        /// subsequent renewal cycles.
        /// </summary>
        /// <remarks>
        /// <para>Authoritative nameservers are part of the zone's delegation at the parent TLD -- they change only when
        /// the domain is transferred to a different DNS provider or when Cloudflare manually reassigns NS pairs (a rare
        /// operational event).  Neither scenario occurs during the lifetime of a running process.  Caching eliminates a
        /// Cloudflare <c>GET /zones/{id}</c> API call on every renewal cycle (typically every 60 days).</para>
        /// <para><see langword="null"/> indicates the nameservers have not yet been resolved, or a previous resolution
        /// attempt failed.  In the latter case <see cref="_authoritativeDnsResolved"/> remains <see langword="false"/>,
        /// allowing the next renewal cycle to retry.  When <see cref="_authoritativeDnsResolved"/> is
        /// <see langword="true"/> and this field is <see langword="null"/>, it indicates an impossible state -- resolution
        /// succeeded with zero IPs, which is handled by the <c>nameservers.Length == 0</c> guard in
        /// <see cref="CreateAuthoritativeDnsClientAsync"/>.</para>
        /// </remarks>
        private LegacyAuthoritativeDnsClient? _cachedAuthoritativeDnsClient;

        /// <summary>
        /// Set to <see langword="true"/> (via <see cref="Volatile.Write(ref bool, bool)"/>) after the first
        /// <em>successful</em> nameserver resolution.  Distinguishes "never resolved" (<see langword="false"/>) from
        /// "resolved and cached" (<see langword="true"/>).
        /// </summary>
        /// <remarks>
        /// <para>Intentionally <em>not</em> set on failure -- a transient network error at startup should not permanently
        /// disable authoritative DNS polling.  The next renewal cycle (typically 6 hours later) retries the Cloudflare API
        /// call.</para>
        /// <para><b>Memory ordering:</b> Written via <see cref="Volatile.Write(ref bool, bool)"/> and read via
        /// <see cref="Volatile.Read(ref bool)"/> to ensure the cached client reference is visible to the reading thread
        /// before the flag is observed as <see langword="true"/>.  The volatile write acts as a release fence, and the
        /// volatile read acts as an acquire fence -- together they form a happens-before relationship that guarantees the
        /// reading thread sees the fully constructed <see cref="_cachedAuthoritativeDnsClient"/> when the flag is
        /// <see langword="true"/>.</para>
        /// </remarks>
        private bool _authoritativeDnsResolved;

        /// <summary>
        /// Guards the one-time initialisation of <see cref="_cachedAuthoritativeDnsClient"/> to prevent duplicate
        /// Cloudflare <c>GET /zones/{id}</c> API calls under hypothetical concurrent access.
        /// </summary>
        /// <remarks>
        /// <para><b>Current callers:</b> <see cref="CreateAuthoritativeDnsClientAsync"/> is currently called exclusively
        /// from <see cref="RequestCertificateAsync"/>, which runs on a single <see cref="CertificateRenewalService"/>
        /// loop -- so concurrent access does not occur today.  The semaphore provides defence-in-depth against future
        /// refactoring that might introduce concurrent callers.</para>
        /// <para><b>Fast path:</b> The <see cref="Volatile.Read(ref bool)"/> check on
        /// <see cref="_authoritativeDnsResolved"/> before the semaphore ensures that after the first successful resolution,
        /// all subsequent calls return the cached client without touching the semaphore -- zero contention on the hot
        /// path.</para>
        /// <para><b>Failure path:</b> If the Cloudflare API call fails, the semaphore is released without setting the
        /// flag, allowing the next renewal cycle's call to retry.  If two threads hypothetically raced past the outer
        /// <see cref="Volatile.Read(ref bool)"/> check simultaneously, one would acquire the semaphore and resolve, the
        /// other would wait, then see the flag is set in the inner double-check and return the cached client
        /// immediately.</para>
        /// <para><b>Disposal:</b> Disposed by <see cref="Dispose"/> during host shutdown.</para>
        /// </remarks>
        private readonly SemaphoreSlim _dnsInitLock = new(1, 1);

        #endregion

        #region Private Methods -- Authoritative DNS Resolution

        /// <summary>
        /// Returns a cached <see cref="AuthoritativeDnsClient"/> configured to query the zone's authoritative nameservers
        /// directly (bypassing recursive resolver caches).  On first call, resolves the nameservers via the Cloudflare
        /// <c>GET /zones/{id}</c> API and caches the result for all subsequent renewal cycles.
        /// </summary>
        /// <remarks>
        /// <para><b>Double-check locking:</b> The method uses a <see cref="Volatile.Read(ref bool)"/> fast path followed
        /// by a <see cref="SemaphoreSlim"/>-guarded initialisation with an inner re-check.  This guarantees exactly one
        /// Cloudflare API call for nameserver resolution, even under hypothetical concurrent access, while keeping the
        /// post-initialisation path lock-free.</para>
        ///
        /// <para><b>Cache lifetime:</b> The cached client persists for the lifetime of this
        /// <see cref="AcmeCertificateProvider"/> instance (which lives for the entire process -- created once in
        /// <see cref="CertificateRenewalService.ExecuteAsync"/>).  Authoritative nameservers are part of the zone's
        /// delegation and do not change during normal operation.</para>
        ///
        /// <para><b>Failure handling:</b> If resolution fails (Cloudflare API error, all NS hostname resolutions fail),
        /// <see cref="_cachedAuthoritativeDnsClient"/> remains <see langword="null"/> and
        /// <see cref="_authoritativeDnsResolved"/> is <em>not</em> set -- allowing the next renewal cycle to retry the
        /// Cloudflare API call.  This handles transient network failures at startup without permanently falling back to
        /// the fixed delay.</para>
        ///
        /// <para>Returns <see langword="null"/> when authoritative nameservers cannot be resolved.  The caller
        /// (<see cref="ProcessDns01ChallengeAsync"/>) falls back to a fixed <see cref="DnsFallbackDelaySeconds"/>
        /// delay.</para>
        /// </remarks>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>An <see cref="AuthoritativeDnsClient"/> configured with the zone's authoritative nameserver IPs, or
        /// <see langword="null"/> if resolution failed.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task<LegacyAuthoritativeDnsClient?> CreateAuthoritativeDnsClientAsync(CancellationToken ct)
        {
            // Fast path -- lock-free after first successful resolution.  Volatile.Read provides an acquire fence ensuring
            // the _cachedAuthoritativeDnsClient reference is visible if the flag is true.
            if (Volatile.Read(ref _authoritativeDnsResolved))
            {
                if (_cachedAuthoritativeDnsClient is not null && logger.IsEnabled(LogLevel.Debug))
                    LogUsingCachedDnsClient(_cachedAuthoritativeDnsClient.NameserverCount);

                return _cachedAuthoritativeDnsClient;
            }

            // Slow path -- acquire the semaphore to ensure exactly one Cloudflare API call.
            await _dnsInitLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring the lock -- another thread may have resolved while we waited.
                if (Volatile.Read(ref _authoritativeDnsResolved))
                {
                    if (_cachedAuthoritativeDnsClient is not null && logger.IsEnabled(LogLevel.Debug))
                        LogUsingCachedDnsClient(_cachedAuthoritativeDnsClient.NameserverCount);

                    return _cachedAuthoritativeDnsClient;
                }

                IPAddress[] nameservers = await ResolveAuthoritativeNameserversAsync(ct).ConfigureAwait(false);

                if (nameservers.Length == 0)
                {
                    // Do NOT set _authoritativeDnsResolved -- allow the next call to retry the Cloudflare API.
                    // A transient network failure should not permanently disable authoritative DNS polling.
                    LogNameserverResolutionFailed(DnsFallbackDelaySeconds);
                    return null;
                }

                // Build the display string outside the log call to avoid allocation when Information is disabled.
                // Direct loop avoids the LINQ Select iterator + delegate allocation from the previous implementation.
                string servers = FormatNameserverIps(nameservers);
                LogResolvedNameservers(nameservers.Length, servers);

                _cachedAuthoritativeDnsClient = new LegacyAuthoritativeDnsClient(nameservers);

                // Volatile.Write acts as a release fence -- ensures the fully constructed AuthoritativeDnsClient is
                // visible to any thread that subsequently reads the flag via Volatile.Read on the fast path.
                Volatile.Write(ref _authoritativeDnsResolved, true);
                return _cachedAuthoritativeDnsClient;
            }
            finally
            {
                _dnsInitLock.Release();
            }
        }

        /// <summary>
        /// Formats an array of <see cref="IPAddress"/> instances into a comma-delimited display string for diagnostic
        /// logging.
        /// </summary>
        /// <remarks>
        /// <para><b>Single-element fast path:</b> When the array contains exactly one IP, the result is returned directly
        /// via <see cref="IPAddress.ToString"/> without allocating a <see cref="StringBuilder"/>.</para>
        ///
        /// <para><b>Allocation:</b> For multi-element arrays, a single <see cref="StringBuilder"/> is allocated with a
        /// capacity hint of <c>nameservers.Length * 16</c> (~15 characters per IPv4 address + separator).  This replaces
        /// the previous LINQ <c>nameservers.Select(ip => ip.ToString())</c> which allocated a LINQ iterator and
        /// delegate.</para>
        ///
        /// <para><b>Not a Utilities candidate:</b> This method is private and called from a single site
        /// (<see cref="CreateAuthoritativeDnsClientAsync"/>).  No other class in the codebase formats IP address arrays
        /// for logging.  Extracting to a shared utility would add indirection without reuse benefit.</para>
        /// </remarks>
        /// <param name="nameservers">The nameserver IP addresses to format.  Must not be empty.</param>
        /// <returns>A comma-delimited string of IP addresses (e.g. <c>"1.2.3.4, 5.6.7.8"</c>).</returns>
        private static string FormatNameserverIps(IPAddress[] nameservers)
        {
            if (nameservers.Length == 1)
                return nameservers[0].ToString();

            StringBuilder sb = new(nameservers.Length * 16); // ~15 chars per IPv4 address + separator
            for (int i = 0; i < nameservers.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(nameservers[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Resolves the zone's authoritative nameservers by querying the Cloudflare <c>GET /zones/{id}</c> API, then
        /// resolving each NS hostname to IP addresses via <see cref="Dns.GetHostAddressesAsync(string, AddressFamily,
        /// CancellationToken)"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>Partial success:</b> Individual NS hostnames that fail DNS resolution are logged at
        /// <see cref="LogLevel.Debug"/> and skipped -- a partial set of nameservers is still useful for propagation polling.
        /// If <em>all</em> NS resolutions fail, the returned array is empty and the caller falls back to a fixed
        /// delay.</para>
        ///
        /// <para><b>IPv4 preference:</b> Each NS hostname is resolved via
        /// <see cref="Dns.GetHostAddressesAsync(string, AddressFamily, CancellationToken)"/> with
        /// <see cref="AddressFamily.InterNetwork"/> (IPv4).  If no IPv4 addresses are returned, a fallback resolution
        /// with <see cref="AddressFamily.InterNetworkV6"/> (IPv6) is attempted.  This avoids a common dual-stack pitfall:
        /// many server environments have IPv6 addresses configured (via SLAAC or DHCPv6) but no working IPv6 route to
        /// external hosts.  When the <see cref="AuthoritativeDnsClient"/> randomly selects an unreachable IPv6 address
        /// during propagation polling, the 5-second UDP receive timeout fires -- wasting time and potentially exhausting
        /// the <see cref="DnsPropagationTimeout"/>.  Preferring IPv4 (universally routable in server environments) avoids
        /// these spurious timeouts while still supporting IPv6-only deployments where IPv4 is unavailable.</para>
        ///
        /// <para><b>De-duplication:</b> Cloudflare typically assigns two NS hostnames (e.g. <c>anna.ns.cloudflare.com</c>,
        /// <c>bob.ns.cloudflare.com</c>), but each may resolve to the same anycast IP.  A <see cref="HashSet{T}"/>
        /// de-duplicates by IP address to avoid redundant queries to the same endpoint during propagation polling.</para>
        ///
        /// <para><b>Cancellation:</b> <see cref="OperationCanceledException"/> from either the Cloudflare API call or a
        /// <see cref="Dns.GetHostAddressesAsync(string, AddressFamily, CancellationToken)"/> call is rethrown rather than
        /// caught by the outer <c>catch (Exception)</c> block, ensuring host-shutdown cancellation propagates immediately
        /// instead of being logged as a warning and swallowed.</para>
        ///
        /// <para><b>Credential safety:</b> Delegates to <see cref="SendCloudflareRequestAsync"/> which handles credential
        /// injection and unconditional scrubbing in a <see langword="finally"/> block.  See the file-level Security comment
        /// for the full rationale.</para>
        ///
        /// <para><b>Response size guard:</b> Enforced by <see cref="SendCloudflareRequestAsync"/> at two levels:
        /// <see cref="EnsureResponseSizeWithinLimit"/> rejects responses whose <c>Content-Length</c> exceeds
        /// <see cref="MaxCloudflareResponseBytes"/> without reading any body bytes;
        /// <see cref="LengthLimitedReadStream"/> wraps the response stream for chunked responses and throws if the
        /// cumulative read exceeds the limit during <see cref="JsonDocument.ParseAsync(Stream, JsonDocumentOptions?,
        /// CancellationToken)"/>.</para>
        ///
        /// <para><b>Schema errors:</b> If the Cloudflare response is valid HTTP 200 but contains unexpected JSON structure
        /// (missing <c>result</c> or <c>name_servers</c> property), the resulting <see cref="KeyNotFoundException"/> is
        /// caught by the outer <c>catch (Exception)</c> block and logged -- the caller falls back to a fixed delay.</para>
        /// </remarks>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>An array of unique nameserver IP addresses, or an empty array if the zone API call or all NS
        /// resolutions failed.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task<IPAddress[]> ResolveAuthoritativeNameserversAsync(CancellationToken ct)
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, $"zones/{options.CloudflareZoneId}");

                using JsonDocument doc = await SendCloudflareRequestAsync(request, "GET /zones/{id}", ct).ConfigureAwait(false);

                JsonElement nameServersElement = doc.RootElement.GetProperty("result").GetProperty("name_servers");

                // Use a HashSet to de-duplicate IPs inline -- avoids the allocation of an intermediate List + a
                // Distinct() LINQ iterator + a ToArray() copy.  Cloudflare NS hostnames often resolve to overlapping
                // anycast IPs.
                HashSet<IPAddress> addresses = [];
                foreach (JsonElement ns in nameServersElement.EnumerateArray())
                {
                    string? hostname = ns.GetString();
                    if (string.IsNullOrWhiteSpace(hostname))
                        continue;

                    try
                    {
                        // Prefer IPv4 to avoid broken-IPv6 timeouts in dual-stack environments.  Many servers have
                        // IPv6 configured (SLAAC/DHCPv6) but no working route -- the AuthoritativeDnsClient's
                        // 5-second UDP timeout would fire on every poll that randomly selects an unreachable IPv6
                        // address, wasting DnsPropagationTimeout budget.  Fall back to IPv6 only when no IPv4
                        // addresses are available (IPv6-only deployments).
                        IPAddress[] resolved = await System.Net.Dns.GetHostAddressesAsync(hostname, AddressFamily.InterNetwork, ct).ConfigureAwait(false);

                        if (resolved.Length == 0)
                            resolved = await System.Net.Dns.GetHostAddressesAsync(hostname, AddressFamily.InterNetworkV6, ct).ConfigureAwait(false);

                        foreach (IPAddress ip in resolved)
                            addresses.Add(ip);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (logger.IsEnabled(LogLevel.Debug))
                            LogNameserverHostnameResolutionFailed(ex, hostname);
                    }
                }

                return [.. addresses];
            }
            catch (OperationCanceledException)
            {
                // Host shutdown -- rethrow so the caller's cancellation handling runs instead of logging a spurious warning.
                throw;
            }
            catch (Exception ex)
            {
                LogCloudflareZoneApiFailed(ex);
                return [];
            }
        }

        #endregion

        #region Private Methods -- DNS Propagation Polling

        /// <summary>
        /// Polls the authoritative nameservers until the specified TXT record is visible, or the
        /// <see cref="DnsPropagationTimeout"/> expires.
        /// </summary>
        /// <remarks>
        /// <para><b>Poll loop:</b> Queries the authoritative DNS client at <see cref="DnsPollInterval"/> intervals.
        /// Individual query failures are logged at <see cref="LogLevel.Debug"/> and retried -- transient DNS errors are
        /// expected during the propagation window (Cloudflare's anycast PoPs may have slightly different propagation
        /// latencies).</para>
        ///
        /// <para><b>Timeout behaviour:</b> If the timeout expires, the method logs a warning and returns -- ACME validation
        /// proceeds anyway.  The record may be visible to Let's Encrypt's own resolvers even if the authoritative NS
        /// returned stale results, so aborting here would unnecessarily fail the renewal.</para>
        ///
        /// <para><b>Matching strategy:</b> The expected TXT value is compared via <see cref="StringComparison.Ordinal"/>
        /// because ACME challenge tokens are base64url-encoded (case-sensitive by definition).  A simple
        /// <c>foreach</c>/<c>Equals</c> loop avoids a <see cref="List{T}.Contains"/> call that would use the default
        /// <see cref="EqualityComparer{T}"/> and is no less readable for this use case.</para>
        ///
        /// <para><b>Monotonic timing:</b> <see cref="Environment.TickCount64"/> provides monotonic millisecond timing
        /// that is immune to wall-clock adjustments (NTP step corrections, DST transitions, manual clock changes).
        /// Unlike <see cref="DateTime.UtcNow"/> or <see cref="Stopwatch"/>, <c>TickCount64</c> is a single 64-bit read
        /// with no <c>QueryPerformanceCounter</c> overhead on Windows and no <c>clock_gettime(CLOCK_MONOTONIC)</c>
        /// syscall overhead on Linux -- the JIT inlines it to a direct TSC or jiffies read on both platforms.</para>
        /// </remarks>
        /// <param name="dnsClient">The authoritative DNS client.</param>
        /// <param name="recordName">The fully-qualified TXT record name (e.g. <c>_acme-challenge.example.com</c>).</param>
        /// <param name="expectedValue">The expected TXT record value (the base64url-encoded ACME challenge digest).</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task WaitForTxtRecordAsync(
            LegacyAuthoritativeDnsClient dnsClient, string recordName, string expectedValue, CancellationToken ct)
        {
            long startTicks = Environment.TickCount64;
            long timeoutMs = (long)DnsPropagationTimeout.TotalMilliseconds;

            if (logger.IsEnabled(LogLevel.Debug))
                LogPollingAuthoritativeDns(recordName);

            while (true)
            {
                try
                {
                    List<string> txtValues = await dnsClient.QueryTxtAsync(recordName, ct).ConfigureAwait(false);

                    foreach (string value in txtValues)
                    {
                        if (value.Equals(expectedValue, StringComparison.Ordinal))
                        {
                            long elapsedMs = Environment.TickCount64 - startTicks;
                            LogTxtRecordVisible(recordName, elapsedMs);
                            return;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                        LogDnsQueryFailed(ex, recordName);
                }

                long elapsed = Environment.TickCount64 - startTicks;
                if (elapsed >= timeoutMs)
                {
                    LogTxtRecordPropagationTimeout(recordName, (int)DnsPropagationTimeout.TotalSeconds);
                    return;
                }

                await Task.Delay(DnsPollInterval, ct).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private Methods -- Cloudflare TXT Record CRUD

        /// <summary>
        /// Creates a TXT DNS record via the Cloudflare <c>POST /zones/{zoneId}/dns_records</c> API.
        /// </summary>
        /// <remarks>
        /// <para><b>Payload:</b> The JSON body specifies <c>type=TXT</c>, the fully-qualified record name, the challenge
        /// digest value, and a low TTL (<see cref="TxtRecordTtlSeconds"/>) so stale records expire quickly from recursive
        /// resolver caches if cleanup fails.</para>
        ///
        /// <para><b>Serialisation:</b> The payload is serialised from a <see cref="CloudflareDnsRecordRequest"/> using the
        /// shared <see cref="CertificateDefaults.JsonOptions"/> (frozen, camelCase naming).  This method runs at most once
        /// per domain per renewal cycle (every 60 days) -- the single short-lived allocation is negligible.</para>
        ///
        /// <para><b>Record ID:</b> The returned Cloudflare record ID is stored by the caller
        /// (<see cref="ProcessDns01ChallengeAsync"/>) for deletion in <see cref="CleanupTxtRecordsAsync"/>.</para>
        ///
        /// <para><b>Null record ID guard:</b> The <c>id</c> field in the Cloudflare response is validated with an explicit
        /// null check rather than the <c>!</c> null-forgiving operator.  Per CONTRIBUTING.md, <c>!</c> is reserved for
        /// DI-guarantee scenarios -- a Cloudflare API response is external input that should be validated defensively.  A
        /// <see langword="null"/> ID would indicate a Cloudflare API contract violation or a malformed response.</para>
        ///
        /// <para><b>Credential safety and response size guard:</b> Handled by <see cref="SendCloudflareRequestAsync"/>.
        /// See file-level Security comment for the full rationale.</para>
        /// </remarks>
        /// <param name="name">The fully-qualified record name (e.g. <c>_acme-challenge.example.com</c>).</param>
        /// <param name="content">The TXT record value (the ACME DNS-01 challenge token digest).</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>The Cloudflare record ID, used for subsequent deletion in <see cref="CleanupTxtRecordsAsync"/>.</returns>
        /// <exception cref="HttpRequestException">Thrown when the Cloudflare API returns a non-success HTTP status
        /// code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the Cloudflare API returns HTTP 200 with
        /// <c>"success": false</c> (logical error -- invalid zone ID, permission denied, rate limit), the response body
        /// exceeds <see cref="MaxCloudflareResponseBytes"/>, or the response contains a <see langword="null"/> record
        /// ID.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task<string> CreateCloudflareTxtRecordAsync(string name, string content, CancellationToken ct)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, $"zones/{options.CloudflareZoneId}/dns_records");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new CloudflareDnsRecordRequest { Type = "TXT", Name = name, Content = content, Ttl = TxtRecordTtlSeconds }, CertificateDefaults.JsonOptions),
                Encoding.UTF8, "application/json");

            using JsonDocument doc = await SendCloudflareRequestAsync(request, "POST /dns_records", ct).ConfigureAwait(false);

            // Validate the record ID explicitly rather than using the ! null-forgiving operator.  The id field is
            // external API input -- a null value indicates a Cloudflare contract violation or malformed response.
            string recordId = doc.RootElement.GetProperty("result").GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Cloudflare POST /dns_records returned a null record ID");

            if (logger.IsEnabled(LogLevel.Debug))
                LogCreatedCloudflareTxtRecord(recordId, name);
            return recordId;
        }

        /// <summary>
        /// Deletes a single TXT DNS record via the Cloudflare <c>DELETE /zones/{zoneId}/dns_records/{recordId}</c> API.
        /// </summary>
        /// <remarks>
        /// <para><b>Consumer:</b> Called by <see cref="CleanupTxtRecordsAsync"/> during best-effort cleanup.  Individual
        /// failures are caught and logged by the caller -- this method propagates all exceptions.</para>
        ///
        /// <para><b>Response validation:</b> The DELETE response body is read and parsed to validate the Cloudflare
        /// envelope's <c>success</c> field via <see cref="EnsureCloudflareSuccess"/>.  Cloudflare returns a result object
        /// even for DELETE operations -- HTTP 200 with <c>"success": false</c> can occur if the record was already deleted
        /// (e.g. by a concurrent cleanup or manual intervention).  Only the <c>success</c> flag is checked; the
        /// <c>result</c> payload is not used.</para>
        ///
        /// <para><b>Credential safety and response size guard:</b> Handled by <see cref="SendCloudflareRequestAsync"/>.
        /// See file-level Security comment for the full rationale.</para>
        /// </remarks>
        /// <param name="recordId">The Cloudflare record ID to delete.</param>
        /// <param name="ct">Cancellation token (typically <see cref="CancellationToken.None"/> during cleanup).</param>
        /// <exception cref="HttpRequestException">Thrown when the Cloudflare API returns a non-success HTTP status
        /// code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the Cloudflare API returns HTTP 200 with
        /// <c>"success": false</c>, or the response body exceeds <see cref="MaxCloudflareResponseBytes"/>.</exception>
        private async Task DeleteCloudflareTxtRecordAsync(string recordId, CancellationToken ct)
        {
            using HttpRequestMessage request = new(HttpMethod.Delete, $"zones/{options.CloudflareZoneId}/dns_records/{recordId}");

            using JsonDocument doc = await SendCloudflareRequestAsync(request, "DELETE /dns_records", ct).ConfigureAwait(false);

            // Response parsed and envelope validated by SendCloudflareRequestAsync -- no further action needed.
            // The result payload is not used for DELETE operations.
        }

        #endregion

        #region Private Methods -- Shared Cloudflare API Helper

        /// <summary>
        /// Sends a Cloudflare API request with credential injection, response size validation, HTTP status validation,
        /// JSON parsing with streaming size limits, and Cloudflare envelope validation.  The bearer token is unconditionally
        /// scrubbed from the request before any exception propagates.
        /// </summary>
        /// <remarks>
        /// <para><b>Rationale:</b> The three Cloudflare API methods (<see cref="ResolveAuthoritativeNameserversAsync"/>,
        /// <see cref="CreateCloudflareTxtRecordAsync"/>, <see cref="DeleteCloudflareTxtRecordAsync"/>) previously
        /// duplicated an identical pattern: set <c>Authorization</c> header -> send -> validate <c>Content-Length</c> ->
        /// ensure HTTP success -> read stream -> wrap in <see cref="LengthLimitedReadStream"/> -> parse JSON -> validate
        /// Cloudflare envelope -> scrub auth header -> dispose response.  This helper centralises all seven steps,
        /// eliminating ~40 lines of duplication per call site and ensuring any future Cloudflare API calls automatically
        /// inherit the same security invariants (credential scrubbing, response size limits).</para>
        ///
        /// <para><b>Credential safety:</b> The <c>Authorization</c> header is set on the
        /// <see cref="HttpRequestMessage"/> immediately before <see cref="HttpClient.SendAsync(HttpRequestMessage,
        /// CancellationToken)"/> and unconditionally cleared in the <see langword="finally"/> block.  This covers all
        /// exception paths: <c>SendAsync</c> failure, <c>EnsureSuccessStatusCode</c>, <c>ReadAsStreamAsync</c>,
        /// <c>JsonDocument.ParseAsync</c>, and <c>EnsureCloudflareSuccess</c>.  The token never persists on the
        /// <see cref="HttpRequestMessage"/> beyond this method's scope.</para>
        ///
        /// <para><b>Response lifecycle:</b> The <see cref="HttpResponseMessage"/> is disposed in the
        /// <see langword="finally"/> block to ensure the underlying HTTP connection is returned to the
        /// <see cref="SocketsHttpHandler"/> pool even on exception paths.  The returned <see cref="JsonDocument"/> is
        /// self-contained (backed by pooled memory, not the response stream), so it remains valid after the response
        /// is disposed.</para>
        ///
        /// <para><b>Response size guard:</b> Enforced at two levels: <see cref="EnsureResponseSizeWithinLimit"/> rejects
        /// responses whose <c>Content-Length</c> exceeds <see cref="MaxCloudflareResponseBytes"/> without reading any body
        /// bytes; <see cref="LengthLimitedReadStream"/> wraps the response stream for chunked responses and throws if the
        /// cumulative read exceeds the limit during JSON parsing.</para>
        ///
        /// <para><b>Validation order:</b> <see cref="EnsureResponseSizeWithinLimit"/> is called <em>before</em>
        /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> to reject oversized responses without reading any
        /// body bytes -- even if the HTTP status is 200.  <c>EnsureSuccessStatusCode</c> is called next to reject
        /// 4xx/5xx responses before any body bytes are read.  This ordering prevents reading potentially malicious body
        /// content from a compromised endpoint that returns HTTP 200 with an enormous payload.</para>
        /// </remarks>
        /// <param name="request">The pre-constructed <see cref="HttpRequestMessage"/> (method, URI, optional content).
        /// The <c>Authorization</c> header is set and cleared by this method -- callers must not set it.</param>
        /// <param name="operation">A short description of the API call (e.g. <c>"POST /dns_records"</c>) for error
        /// messages.  Must not contain credentials or infrastructure identifiers.</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>A parsed <see cref="JsonDocument"/> whose Cloudflare envelope <c>success</c> field has been validated.
        /// The caller owns the document and must dispose it.</returns>
        /// <exception cref="HttpRequestException">Thrown when the Cloudflare API returns a non-success HTTP status
        /// code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the Cloudflare API returns HTTP 200 with
        /// <c>"success": false</c>, or the response body exceeds <see cref="MaxCloudflareResponseBytes"/>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
        private async Task<JsonDocument> SendCloudflareRequestAsync(HttpRequestMessage request, string operation, CancellationToken ct)
        {
            request.Headers.Authorization = new("Bearer", options.CloudflareApiToken);

            HttpResponseMessage? response = null;
            try
            {
                response = await CloudflareHttpClient.SendAsync(request, ct).ConfigureAwait(false);

                EnsureResponseSizeWithinLimit(response, operation);
                response.EnsureSuccessStatusCode();

                Stream responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using (responseStream.ConfigureAwait(false))
                {
                    using LengthLimitedReadStream limitedStream = new(responseStream, MaxCloudflareResponseBytes, operation, logger);
                    JsonDocument doc = await JsonDocument.ParseAsync(limitedStream, cancellationToken: ct)
                        .ConfigureAwait(false);

                    EnsureCloudflareSuccess(doc, operation);

                    return doc;
                }
            }
            finally
            {
                // Unconditionally scrub the bearer token before any exception propagates.  This covers all throw paths:
                // SendAsync failure, EnsureSuccessStatusCode, ReadAsStreamAsync, JsonDocument.ParseAsync, and
                // EnsureCloudflareSuccess.
                request.Headers.Authorization = null;
                response?.Dispose();
            }
        }

        #endregion

        #region Private Methods -- Cloudflare Response Validation

        /// <summary>
        /// Validates that a Cloudflare API response declares a <c>Content-Length</c> within
        /// <see cref="MaxCloudflareResponseBytes"/>.  This is the first of two size enforcement levels -- it rejects
        /// obviously oversized responses upfront without reading any body bytes.
        /// </summary>
        /// <remarks>
        /// <para><b>Content-Length check:</b> Only responses with a <c>Content-Length</c> header are validated at this
        /// level.  Chunked transfer-encoded responses (where <c>ContentLength</c> is <see langword="null"/>) pass this
        /// check and are instead guarded by the <see cref="LengthLimitedReadStream"/> wrapper during streaming reads --
        /// see <see cref="SendCloudflareRequestAsync"/>.</para>
        /// </remarks>
        /// <param name="response">The HTTP response to validate.</param>
        /// <param name="operation">A short description of the API call for the error message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the <c>Content-Length</c> exceeds
        /// <see cref="MaxCloudflareResponseBytes"/>.</exception>
        private static void EnsureResponseSizeWithinLimit(HttpResponseMessage response, string operation)
        {
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxCloudflareResponseBytes)
            {
                throw new InvalidOperationException(
                    $"Cloudflare API {operation} response body is {contentLength.Value:N0} bytes, " +
                    $"exceeding the {MaxCloudflareResponseBytes:N0}-byte safety limit -- possible compromised endpoint");
            }
        }

        /// <summary>
        /// Validates that a Cloudflare API response envelope indicates success.  Cloudflare can return HTTP 200 with
        /// <c>"success": false</c> for logical errors (invalid zone ID, permission denied, rate limits), so checking the
        /// HTTP status code alone is insufficient.
        /// </summary>
        /// <remarks>
        /// <para><b>Two-level validation:</b> Every Cloudflare API call in this class validates responses at two levels:
        /// first the HTTP status code via <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>, then the JSON
        /// envelope's <c>success</c> field via this method.  This catches both HTTP-level errors (5xx, 4xx) and
        /// Cloudflare-specific logical failures that return HTTP 200.</para>
        ///
        /// <para><b>Error aggregation:</b> When <c>success</c> is <see langword="false"/>, <em>all</em> errors in the
        /// <c>errors</c> array are aggregated into the exception message (semicolon-delimited).  Cloudflare can return
        /// multiple concurrent errors -- for example, a misconfigured API token may produce both a permission error and a
        /// zone-scope error in the same response.  Including all errors avoids masking secondary failures that provide
        /// critical diagnostic context.</para>
        ///
        /// <para><b>Error format:</b> Each error's <c>code</c> (integer) and <c>message</c> (string) are included in the
        /// format <c>[code] message</c>.  If an error lacks a <c>code</c> field, only the message is used.  If the
        /// <c>errors</c> array is empty, missing, or contains no parseable entries, a generic <c>"unknown error"</c>
        /// message is used to ensure the exception always contains actionable text.</para>
        ///
        /// <para><b>Truncation safety:</b> The aggregated error detail is capped at
        /// <see cref="MaxCloudflareErrorDetailLength"/> characters.  Truncation occurs at error-entry boundaries (not
        /// mid-string) so partial error codes and separator fragments are never emitted.  Truncated output is suffixed
        /// with <c>" [truncated]"</c> to signal incomplete data in diagnostics.</para>
        ///
        /// <para><b>Allocation optimisation:</b> The <c>codePrefix</c> string allocation and <c>entryLength</c>
        /// calculation are deferred until after the truncation check confirms the entry will be appended.  This avoids
        /// a short-lived heap allocation for error entries that would be discarded due to truncation.</para>
        /// </remarks>
        /// <param name="doc">The parsed Cloudflare JSON response.</param>
        /// <param name="operation">A short description of the API call (e.g. <c>"POST /dns_records"</c>) for the error
        /// message.  Must not contain credentials or infrastructure identifiers.</param>
        /// <exception cref="InvalidOperationException">Thrown when <c>"success"</c> is <see langword="false"/> or
        /// absent.</exception>
        private static void EnsureCloudflareSuccess(JsonDocument doc, string operation)
        {
            if (doc.RootElement.TryGetProperty("success", out JsonElement successElement) && successElement.GetBoolean())
                return;

            // Build the error detail string from the "errors" array with a hard length cap to prevent unbounded growth
            // from a malformed or adversarial response.  Truncation occurs at error-entry boundaries -- not mid-string --
            // to avoid partial error codes or dangling separators in the diagnostic output.
            string errorDetail = "unknown error";
            if (doc.RootElement.TryGetProperty("errors", out JsonElement errorsElement)
                && errorsElement.ValueKind == JsonValueKind.Array)
            {
                StringBuilder sb = new();
                bool truncated = false;

                foreach (JsonElement error in errorsElement.EnumerateArray())
                {
                    string? message = error.TryGetProperty("message", out JsonElement msgEl) ? msgEl.GetString() : null;
                    if (message is null)
                        continue;

                    // Pre-calculate the candidate entry length to check truncation before allocating the code prefix.
                    // The entry format is: "; [code] message" or "; message" (separator omitted for the first entry).
                    bool hasCode = error.TryGetProperty("code", out JsonElement codeEl) && codeEl.ValueKind == JsonValueKind.Number;

                    // Estimate the entry length without allocating the code prefix string yet.
                    // Code prefix format: "[" + digits + "] " -- at most "[2147483647] " = 14 characters for int.MaxValue.
                    int separatorLength = sb.Length > 0 ? 2 : 0;   // "; " separator
                    int codePrefixEstimate = hasCode ? 14 : 0;     // conservative upper bound for "[int] "
                    int entryLengthEstimate = separatorLength + codePrefixEstimate + message.Length;

                    // Check if appending this entry would exceed the cap.  If so, mark as truncated and stop -- the
                    // previous entries already in sb form a complete, well-formatted string.
                    if (sb.Length + entryLengthEstimate > MaxCloudflareErrorDetailLength)
                    {
                        truncated = true;
                        break;
                    }

                    // Entry will fit -- now allocate the code prefix if needed.
                    if (sb.Length > 0)
                        sb.Append("; ");

                    if (hasCode)
                        sb.Append('[').Append(codeEl.GetInt32()).Append("] ");

                    sb.Append(message);
                }

                if (truncated)
                    sb.Append(" [truncated]");

                if (sb.Length > 0)
                    errorDetail = sb.ToString();
            }

            throw new InvalidOperationException($"Cloudflare API {operation} failed: {errorDetail}");
        }

        #endregion

        #region Private Methods -- TXT Record Cleanup

        /// <summary>
        /// Best-effort cleanup of all TXT records created during the current renewal cycle.  Deletions are performed
        /// concurrently (bounded to <see cref="CloudflareMaxConcurrentDeletes"/> parallel calls) since each record is
        /// independent.  Individual failures are logged at <see cref="LogLevel.Warning"/> and swallowed so that a single
        /// failed deletion does not prevent cleanup of remaining records.
        /// </summary>
        /// <remarks>
        /// <para><b>Uncancellable by design:</b> This method is called with <see cref="CancellationToken.None"/> from
        /// <see cref="RequestCertificateAsync"/>'s <c>finally</c> block.  Orphaned <c>_acme-challenge</c> TXT records
        /// pollute the DNS zone and may interfere with future renewals if Let's Encrypt encounters stale challenge
        /// responses.  The low <see cref="TxtRecordTtlSeconds"/> TTL mitigates this (records auto-expire in 2 minutes),
        /// but explicit cleanup is preferred.</para>
        ///
        /// <para><b>Bounded concurrency:</b> Each Cloudflare DELETE call is independent, but unbounded parallelism risks
        /// tripping Cloudflare's per-IP or per-zone burst rate limits on large multi-SAN orders.
        /// <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, ParallelOptions, Func{TSource,
        /// CancellationToken, ValueTask})"/> caps inflight requests to <see cref="CloudflareMaxConcurrentDeletes"/> (5),
        /// reducing total cleanup time from <c>N x roundtrip</c> to roughly <c>ceil(N/5) x roundtrip</c> while staying
        /// well within Cloudflare's rate envelope of 1,200 requests per 5 minutes per zone.</para>
        ///
        /// <para><b>Error isolation:</b> Each deletion runs in its own <c>try</c>/<c>catch</c> with an
        /// <see cref="OperationCanceledException"/> filter for defence-in-depth.  Although the current sole call site
        /// passes <see cref="CancellationToken.None"/>, the filter ensures that if a future caller passes a real token,
        /// cancellation propagates correctly rather than being swallowed and logged as a cleanup failure.</para>
        /// </remarks>
        /// <param name="records">The record name/ID pairs accumulated during the renewal cycle.</param>
        /// <param name="ct">Cancellation token (typically <see cref="CancellationToken.None"/>).</param>
        private async Task CleanupTxtRecordsAsync(
            List<(string recordName, string recordId)> records, CancellationToken ct)
        {
            if (records.Count == 0)
                return;

            await Parallel.ForEachAsync(
                records,
                new ParallelOptions { MaxDegreeOfParallelism = CloudflareMaxConcurrentDeletes, CancellationToken = ct },
                async (record, token) =>
                {
                    try
                    {
                        await DeleteCloudflareTxtRecordAsync(record.recordId, token).ConfigureAwait(false);

                        if (logger.IsEnabled(LogLevel.Debug))
                            LogCleanedUpTxtRecord(record.recordName);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogTxtRecordCleanupFailed(ex, record.recordName, record.recordId);
                    }
                }).ConfigureAwait(false);
        }

        #endregion
    }

}
