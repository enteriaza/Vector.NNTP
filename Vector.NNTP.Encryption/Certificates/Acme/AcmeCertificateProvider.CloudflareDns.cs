// <copyright file="AcmeCertificateProvider.CloudflareDns.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// AcmeCertificateProvider.CloudflareDns.cs — Cloudflare REST API interactions: TXT record
// creation and deletion.
//
// All Cloudflare API calls use the shared static CloudflareHttpClient (defined in the primary partial).  Responses are
// validated at two levels: HTTP status code, then the Cloudflare envelope's "success" field -- Cloudflare can return
// HTTP 200 with "success": false for logical errors.
//
// Responsibilities:
//   CreateCloudflareTxtRecordAsync      -- POST /dns_records (TXT record for _acme-challenge).
//   DeleteCloudflareTxtRecordAsync      -- DELETE /dns_records/{id} (single record cleanup).
//   SendCloudflareRequestAsync          -- Shared helper encapsulating credential injection, response size validation,
//                                          envelope validation, and credential scrubbing for all Cloudflare API calls.
//   EnsureCloudflareSuccess             -- Validate the Cloudflare JSON envelope's "success" field.
//   CleanupTxtRecordsAsync              -- Bounded-concurrency best-effort deletion of all TXT records created during
//                                          a renewal cycle.
//
// Concurrency:
//   CleanupTxtRecordsAsync uses Parallel.ForEachAsync with bounded concurrency (CloudflareMaxConcurrentDeletes)
//   to delete TXT records in parallel while staying within Cloudflare's rate limits.
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
//   Not applicable.  This file performs HTTP API calls, JSON parsing, and string formatting.  There are no contiguous
//   memory buffers, byte-level pattern searches, or bulk numeric operations that would benefit from vector instructions.
//
// Callers (all within other AcmeCertificateProvider partials):
//   RequestCertificateAsync -> CreateCloudflareTxtRecordAsync, CleanupTxtRecordsAsync

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Vector.NNTP.Encryption.Acme;
using Vector.NNTP.Encryption.Telemetry;
using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Encryption.Certificates.Acme
{
    /// <summary>
    /// Provides functionality for managing ACME certificate issuance and renewal using Cloudflare's DNS.
    /// </summary>
    /// <remarks>Handles interaction with Cloudflare's API to manage DNS records necessary for ACME
    /// challenges, including creating, updating, and deleting TXT records.</remarks>
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

        #region Private Methods -- Cloudflare TXT Record CRUD

        /// <summary>
        /// Creates or updates a TXT DNS record for an ACME DNS-01 challenge via the Cloudflare API.
        /// </summary>
        /// <remarks>
        /// <para><b>Upsert:</b> If a TXT record with the same name already exists (for example from a prior failed
        /// renewal), the record is updated via <c>PATCH</c> instead of <c>POST</c> to avoid HTTP 400 duplicate-name errors.</para>
        ///
        /// <para><b>Name normalization:</b> Challenge FQDNs are converted to zone-relative names via
        /// <see cref="CloudflareDnsRecordNaming"/> (for example <c>_acme-challenge.usenet.ninja</c> →
        /// <c>_acme-challenge</c>).</para>
        ///
        /// <para><b>Payload:</b> The JSON body specifies <c>type=TXT</c>, the record name, the challenge digest value,
        /// <c>proxied=false</c>, and a low TTL (<see cref="TxtRecordTtlSeconds"/>).</para>
        ///
        /// <para><b>Serialisation:</b> The payload is serialised from a <see cref="CloudflareDnsRecordRequest"/> using the
        /// shared <see cref="CertificateDefaults.JsonOptions"/> (frozen, camelCase naming).  This method runs at most once
        /// per domain per renewal cycle (every 60 days) -- the single short-lived allocation is negligible.</para>
        ///
        /// <para><b>Record ID:</b> The returned Cloudflare record ID is stored by the caller
        /// (<see cref="RequestCertificateAsync"/>) for deletion in <see cref="CleanupTxtRecordsAsync"/>.</para>
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
            string apiName = CloudflareDnsRecordNaming.NormalizeTxtRecordNameForApi(name, options.DomainNames);
            string? existingId = await TryFindCloudflareTxtRecordIdAsync(apiName, name, ct).ConfigureAwait(false);
            return existingId is not null
                ? await UpdateCloudflareTxtRecordAsync(existingId, apiName, content, ct).ConfigureAwait(false)
                : await CreateCloudflareTxtRecordCoreAsync(apiName, content, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Attempts to retrieve the Cloudflare TXT record ID for the specified API or fully qualified domain name
        /// asynchronously.
        /// </summary>
        /// <param name="apiName">The API name to search for the TXT record ID.</param>
        /// <param name="fqdnName">The fully qualified domain name to search for the TXT record ID.</param>
        /// <param name="ct">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the TXT record ID if found;
        /// otherwise, null.</returns>
        private async Task<string?> TryFindCloudflareTxtRecordIdAsync(string apiName, string fqdnName, CancellationToken ct)
        {
            if (string.Equals(apiName, fqdnName, StringComparison.OrdinalIgnoreCase))
            {
                return await TryFindCloudflareTxtRecordIdByNameAsync(apiName, ct).ConfigureAwait(false);
            }

            string? relativeMatch = await TryFindCloudflareTxtRecordIdByNameAsync(apiName, ct).ConfigureAwait(false);
            return relativeMatch is not null
                ? relativeMatch
                : await TryFindCloudflareTxtRecordIdByNameAsync(fqdnName, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously searches for a Cloudflare TXT DNS record by name and returns its identifier if found.
        /// </summary>
        /// <param name="recordName">The DNS record name to search for.</param>
        /// <param name="ct">A token to monitor for cancellation requests.</param>
        /// <returns>The identifier of the TXT record if found; otherwise, null.</returns>
        private async Task<string?> TryFindCloudflareTxtRecordIdByNameAsync(string recordName, CancellationToken ct)
        {
            string encodedName = Uri.EscapeDataString(recordName);
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"zones/{options.CloudflareZoneId}/dns_records?type=TXT&name={encodedName}");
            using JsonDocument doc = await SendCloudflareRequestAsync(request, "GET /dns_records", ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("result", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement item in results.EnumerateArray())
            {
                if (item.TryGetProperty("id", out JsonElement idElement))
                {
                    return idElement.GetString();
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a new TXT DNS record via the Cloudflare <c>POST /zones/{zoneId}/dns_records</c> API.
        /// </summary>
        /// <remarks>
        /// <para><b>Consumer:</b> Called by <see cref="ValidateChallengeAsync"/> during ACME DNS-01 challenge validation.
        /// This method sends a JSON payload with the DNS record name and content to Cloudflare, then extracts and returns
        /// the record ID from the response for later reference or cleanup.</para>
        /// <para><b>Error handling:</b> Propagates exceptions (including JSON parsing errors) to the caller; no retry logic.</para>
        /// </remarks>
        /// <param name="apiName">The DNS record name (FQDN) to create in Cloudflare.</param>
        /// <param name="content">The TXT record content value (typically the ACME challenge token).</param>
        /// <param name="ct">Cancellation token to stop the operation.</param>
        /// <returns>The Cloudflare DNS record ID assigned to the newly created record.</returns>
        private async Task<string> CreateCloudflareTxtRecordCoreAsync(string apiName, string content, CancellationToken ct)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, $"zones/{options.CloudflareZoneId}/dns_records");
            request.Content = new StringContent(
                JsonSerializer.Serialize(BuildTxtRecordRequest(apiName, content), CertificateDefaults.JsonOptions),
                Encoding.UTF8,
                "application/json");

            using JsonDocument doc = await SendCloudflareRequestAsync(request, "POST /dns_records", ct).ConfigureAwait(false);
            string recordId = ReadCloudflareRecordId(doc, "POST /dns_records");
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogCreatedCloudflareTxtRecord(recordId, apiName);
            }

            return recordId;
        }

        /// <summary>
        /// Updates an existing TXT DNS record via the Cloudflare <c>PATCH /zones/{zoneId}/dns_records/{recordId}</c> API.
        /// </summary>
        /// <remarks>
        /// <para><b>Consumer:</b> Called by <see cref="CreateCloudflareTxtRecordAsync"/> when a TXT record with the same name
        /// already exists (for example from a prior failed renewal attempt). This avoids HTTP 400 duplicate-name errors by
        /// updating the existing record instead of creating a new one.</para>
        /// <para><b>Payload:</b> Sends a JSON body with <c>type=TXT</c>, the record name, the challenge digest value,
        /// <c>proxied=false</c>, and the low TTL (<see cref="TxtRecordTtlSeconds"/>).</para>
        /// <para><b>Serialisation:</b> The payload is serialised from a <see cref="CloudflareDnsRecordRequest"/> using the
        /// shared <see cref="CertificateDefaults.JsonOptions"/> (frozen, camelCase naming).</para>
        /// <para><b>Record ID extraction:</b> The updated record ID is extracted from the Cloudflare response and returned
        /// to the caller for potential reference in cleanup operations.</para>
        /// <para><b>Credential safety and response size guard:</b> Handled by <see cref="SendCloudflareRequestAsync"/>.
        /// See file-level Security comment for the full rationale.</para>
        /// </remarks>
        /// <param name="recordId">The Cloudflare record ID to update.</param>
        /// <param name="apiName">The DNS record name to update in Cloudflare.</param>
        /// <param name="content">The new TXT record value (the ACME DNS-01 challenge token digest).</param>
        /// <param name="ct">Cancellation token for host shutdown.</param>
        /// <returns>The Cloudflare DNS record ID for the updated record.</returns>
        /// <exception cref="HttpRequestException">Thrown when the Cloudflare API returns a non-success HTTP status
        /// code.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the Cloudflare API returns HTTP 200 with
        /// <c>"success": false</c> (logical error -- invalid zone ID, permission denied, rate limit), the response body
        /// exceeds <see cref="MaxCloudflareResponseBytes"/>, or the response contains a <see langword="null"/> record
        /// ID.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled (host
        /// shutdown).</exception>
        private async Task<string> UpdateCloudflareTxtRecordAsync(
            string recordId,
            string apiName,
            string content,
            CancellationToken ct)
        {
            using HttpRequestMessage request = new(
                HttpMethod.Patch,
                $"zones/{options.CloudflareZoneId}/dns_records/{recordId}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(BuildTxtRecordRequest(apiName, content), CertificateDefaults.JsonOptions),
                Encoding.UTF8,
                "application/json");

            using JsonDocument doc = await SendCloudflareRequestAsync(request, "PATCH /dns_records", ct).ConfigureAwait(false);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogCreatedCloudflareTxtRecord(recordId, apiName);
            }

            return ReadCloudflareRecordId(doc, "PATCH /dns_records");
        }

        /// <summary>
        /// Constructs a Cloudflare DNS record request payload for a TXT record.
        /// </summary>
        /// <remarks>
        /// <para><b>Consumer:</b> Called by <see cref="CreateCloudflareTxtRecordCoreAsync"/> and
        /// <see cref="UpdateCloudflareTxtRecordAsync"/> to build the JSON request body for POST and PATCH operations.</para>
        /// <para><b>Payload structure:</b> Constructs a <see cref="CloudflareDnsRecordRequest"/> with fixed properties:
        /// <c>type=TXT</c>, <c>proxied=false</c>, and <c>ttl=<see cref="TxtRecordTtlSeconds"/></c> (typically 120 seconds
        /// for rapid propagation and automatic cleanup). The <c>name</c> and <c>content</c> parameters are populated from
        /// the caller's arguments.</para>
        /// <para><b>No serialisation:</b> This method returns a POCO object; JSON serialisation is performed by the
        /// caller via <see cref="JsonSerializer.Serialize{TValue}(TValue, System.Text.Json.JsonSerializerOptions?)"/>
        /// using <see cref="CertificateDefaults.JsonOptions"/> (camelCase naming policy).</para>
        /// <para><b>Allocation:</b> A single <see cref="CloudflareDnsRecordRequest"/> object is allocated per ACME
        /// challenge (at most once per domain per renewal cycle). This is a cold-path operation that occurs every 60 days
        /// per domain.</para>
        /// </remarks>
        /// <param name="apiName">The zone-relative or fully-qualified DNS record name (e.g. <c>_acme-challenge</c> or
        /// <c>_acme-challenge.example.com</c>).</param>
        /// <param name="content">The TXT record value, typically a base64url-encoded ACME DNS-01 challenge token digest.</param>
        /// <returns>A <see cref="CloudflareDnsRecordRequest"/> with the specified name and content, fixed type and proxied
        /// settings, and the configured TTL.</returns>
        private CloudflareDnsRecordRequest BuildTxtRecordRequest(string apiName, string content)
        {
            return new()
            {
                Type = "TXT",
                Name = apiName,
                Content = content,
                Ttl = TxtRecordTtlSeconds,
                Proxied = false,
            };
        }

        /// <summary>
        /// Extracts the Cloudflare DNS record ID from a successful API response envelope.
        /// </summary>
        /// <remarks>
        /// <para><b>Consumer:</b> Called by <see cref="CreateCloudflareTxtRecordCoreAsync"/> and
        /// <see cref="UpdateCloudflareTxtRecordAsync"/> after a successful POST or PATCH operation to extract the record ID
        /// for later reference in cleanup operations.</para>
        /// <para><b>Navigation:</b> Traverses the Cloudflare response structure: <c>doc.RootElement</c> → <c>"result"</c>
        /// property → <c>"id"</c> property → string value. Both properties are required; missing or null values throw
        /// <see cref="InvalidOperationException"/>.</para>
        /// <para><b>Null-forgiving rationale:</b> The record ID is extracted via <see cref="JsonElement.GetString()"/>,
        /// which returns <see langword="null"/> if the element is not a string. Per CONTRIBUTING.md, the <c>!</c>
        /// null-forgiving operator is reserved for DI-guarantee scenarios. Here, a <see langword="null"/> ID indicates a
        /// Cloudflare API contract violation or a malformed response, so an explicit null check with a descriptive error
        /// message is more defensive than the operator.</para>
        /// <para><b>Error context:</b> The <paramref name="operation"/> parameter (e.g. <c>"POST /dns_records"</c>) is
        /// included in the exception message for diagnostic clarity, allowing log consumers to identify which API call
        /// produced the unexpected response.</para>
        /// </remarks>
        /// <param name="doc">The parsed Cloudflare JSON response, already validated by <see cref="SendCloudflareRequestAsync"/>
        /// for HTTP status and the <c>"success"</c> field.</param>
        /// <param name="operation">A short description of the API call (e.g. <c>"POST /dns_records"</c> or <c>"PATCH
        /// /dns_records"</c>) for the error message. Must not contain credentials or infrastructure identifiers.</param>
        /// <returns>The Cloudflare-assigned record ID string.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the <c>"result"</c> or <c>"id"</c> property is missing from
        /// the response envelope.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the <c>"id"</c> property value is <see langword="null"/>
        /// (indicating a Cloudflare API contract violation or malformed response).</exception>
        private string ReadCloudflareRecordId(JsonDocument doc, string operation)
        {
            return doc.RootElement.GetProperty("result").GetProperty("id").GetString()
                ?? throw new InvalidOperationException($"Cloudflare {operation} returned a null record ID");
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
        /// <para><b>Rationale:</b> Cloudflare API methods (<see cref="CreateCloudflareTxtRecordAsync"/>,
        /// <see cref="DeleteCloudflareTxtRecordAsync"/>, and related helpers) previously duplicated an identical pattern: set <c>Authorization</c> header -> send -> validate <c>Content-Length</c> ->
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
            using Activity? activity = EncryptionTelemetry.ActivitySource.StartActivity(
                "encryption.acme.cloudflare",
                ActivityKind.Client);
            _ = activity?.SetTag("encryption.acme.operation", operation);

            request.Headers.Authorization = new("Bearer", options.CloudflareApiToken);

            HttpResponseMessage? response = null;
            try
            {
                response = await CloudflareHttpClient.SendAsync(request, ct).ConfigureAwait(false);

                EnsureResponseSizeWithinLimit(response, operation);
                if (!response.IsSuccessStatusCode)
                {
                    string errorDetail = await ReadCloudflareHttpErrorDetailAsync(response, operation, ct).ConfigureAwait(false);
                    throw new HttpRequestException(
                        $"Cloudflare {operation} returned {(int)response.StatusCode} {response.ReasonPhrase}: {errorDetail}");
                }

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
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Aggregated Cloudflare error detail text.</returns>
        private async Task<string> ReadCloudflareHttpErrorDetailAsync(
            HttpResponseMessage response,
            string operation,
            CancellationToken ct)
        {
            try
            {
                Stream responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using (responseStream.ConfigureAwait(false))
                {
                    using LengthLimitedReadStream limitedStream = new(
                        responseStream,
                        MaxCloudflareResponseBytes,
                        operation,
                        logger);
                    using JsonDocument doc = await JsonDocument.ParseAsync(limitedStream, cancellationToken: ct)
                        .ConfigureAwait(false);
                    return FormatCloudflareErrorDetail(doc);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return $"could not read error body ({ex.GetType().Name})";
            }
        }

        /// <summary>
        /// Validates that the HTTP response content size does not exceed the maximum allowed limit.
        /// </summary>
        /// <param name="response">The HTTP response message to check.</param>
        /// <param name="operation">The operation name for error reporting.</param>
        /// <exception cref="InvalidOperationException">Thrown if the response content size exceeds the maximum allowed limit.</exception>
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
            {
                return;
            }

            throw new InvalidOperationException($"Cloudflare API {operation} failed: {FormatCloudflareErrorDetail(doc)}");
        }

        /// <summary>
        /// Formats error messages from a Cloudflare JSON response into a single string.
        /// </summary>
        /// <param name="doc">The JSON document containing Cloudflare error information.</param>
        /// <returns>A formatted string of error messages, or "unknown error" if no errors are present.</returns>
        private static string FormatCloudflareErrorDetail(JsonDocument doc)
        {
            string errorDetail = "unknown error";
            if (!doc.RootElement.TryGetProperty("errors", out JsonElement errorsElement)
                || errorsElement.ValueKind != JsonValueKind.Array)
            {
                return errorDetail;
            }

            StringBuilder sb = new();
            bool truncated = false;

            foreach (JsonElement error in errorsElement.EnumerateArray())
            {
                string? message = error.TryGetProperty("message", out JsonElement msgEl) ? msgEl.GetString() : null;
                if (message is null)
                {
                    continue;
                }

                bool hasCode = error.TryGetProperty("code", out JsonElement codeEl) && codeEl.ValueKind == JsonValueKind.Number;
                int separatorLength = sb.Length > 0 ? 2 : 0;
                int codePrefixEstimate = hasCode ? 14 : 0;
                int entryLengthEstimate = separatorLength + codePrefixEstimate + message.Length;
                if (sb.Length + entryLengthEstimate > MaxCloudflareErrorDetailLength)
                {
                    truncated = true;
                    break;
                }

                if (sb.Length > 0)
                {
                    _ = sb.Append("; ");
                }

                if (hasCode)
                {
                    _ = sb.Append('[').Append(codeEl.GetInt32()).Append("] ");
                }

                _ = sb.Append(message);
            }

            if (truncated)
            {
                _ = sb.Append(" [truncated]");
            }

            return sb.Length > 0 ? sb.ToString() : errorDetail;
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
