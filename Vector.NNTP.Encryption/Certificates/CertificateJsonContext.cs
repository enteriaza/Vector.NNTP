// <copyright file="CertificateJsonContext.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// CertificateJsonContext.cs — Source-generated JSON serialisation context for the certificate subsystem.
//
// Provides compile-time metadata for types serialised via CertificateDefaults.JsonOptions, eliminating reflection-based
// serialisation which is disabled in this application (JsonSerializerIsReflectionEnabledByDefault=false).
//
// Registered types:
//   CloudflareDnsRecordRequest — Cloudflare POST /dns_records request body.
//
// Callers:
//   CertificateDefaults.CreateJsonOptions — assigned as the TypeInfoResolver on the shared JsonSerializerOptions.
//
// Cross-platform:
//   Fully portable.  System.Text.Json source generation produces identical metadata on all .NET 8 runtimes.
//   No P/Invoke, no OS-specific APIs, no architecture-specific intrinsics.  Compatible with Windows (x64) and
//   Linux (x64) on .NET 8.
//
// SIMD applicability:
//   Not applicable.  This file defines a JSON serialisation context and a small DTO.  There are no contiguous
//   memory buffers, byte-level searches, or vectorisable loops.

using System.Text.Json.Serialization;

namespace Vector.NNTP.Encryption.Certificates
{

    /// <summary>
    /// Source-generated <see cref="JsonSerializerContext"/> for the certificate subsystem.  Provides compile-time JSON
    /// metadata for all types serialised via <see cref="CertificateDefaults.JsonOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why source generation:</b> This application disables reflection-based JSON serialisation via
    /// <c>JsonSerializerIsReflectionEnabledByDefault=false</c>.  Without an explicit <see cref="JsonSerializerContext"/>,
    /// the <see cref="System.Text.Json.JsonSerializerOptions.MakeReadOnly(bool)"/> call in
    /// <see cref="CertificateDefaults.CreateJsonOptions"/> throws <see cref="InvalidOperationException"/> when
    /// attempting to populate the reflection-based resolver.</para>
    ///
    /// <para><b>Registered types:</b></para>
    /// <list type="bullet">
    ///   <item><description><see cref="CloudflareDnsRecordRequest"/> — Cloudflare <c>POST /zones/{zoneId}/dns_records</c>
    ///     request body for ACME DNS-01 challenge TXT records.</description></item>
    /// </list>
    ///
    /// <para><b>Naming policy:</b> <see cref="System.Text.Json.JsonNamingPolicy.CamelCase"/> is applied at the
    /// <see cref="System.Text.Json.JsonSerializerOptions"/> level in <see cref="CertificateDefaults.CreateJsonOptions"/>,
    /// not at the context level.  The source generator respects the options-level policy at runtime, keeping the naming
    /// convention in a single place.</para>
    ///
    /// <para><b>Thread safety:</b> The <see cref="Default"/> singleton is created once by the source generator and is
    /// immutable.  Safe for concurrent access from any thread.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  The source generator emits platform-independent C# code at compile
    /// time.  No runtime differences between Windows and Linux.</para>
    /// </remarks>
    [JsonSerializable(typeof(CloudflareDnsRecordRequest))]
    internal partial class CertificateJsonContext : JsonSerializerContext
    {
    }

    /// <summary>
    /// Concrete request body for the Cloudflare <c>POST /zones/{zoneId}/dns_records</c> API, replacing the anonymous type
    /// previously used in <see cref="AcmeCertificateProvider.CreateCloudflareTxtRecordAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a concrete type:</b> The System.Text.Json source generator cannot produce metadata for anonymous types.
    /// Extracting the payload into a named type allows <see cref="CertificateJsonContext"/> to include it in compile-time
    /// code generation.</para>
    ///
    /// <para><b>Serialisation only:</b> This type is only serialised (via
    /// <see cref="System.Text.Json.JsonSerializer.Serialize{TValue}(TValue, System.Text.Json.JsonSerializerOptions?)"/>
    /// in <see cref="AcmeCertificateProvider.CreateCloudflareTxtRecordAsync"/>), never deserialised from an inbound JSON
    /// response.  Properties use <see langword="init"/> setters to enforce single-assignment semantics at construction time
    /// and prevent accidental mutation after the object initialiser completes.</para>
    ///
    /// <para><b>Wire format:</b> With <see cref="System.Text.Json.JsonNamingPolicy.CamelCase"/> applied via
    /// <see cref="CertificateDefaults.JsonOptions"/>, the properties serialise to lowercase JSON keys matching the
    /// Cloudflare API convention: <c>{"type":"TXT","name":"...","content":"...","ttl":60}</c>.</para>
    ///
    /// <para><b>Allocation frequency:</b> Instantiated at most once per domain per renewal cycle (every ~60 days).
    /// The short-lived allocation is negligible.</para>
    ///
    /// <para><b>Thread safety:</b> Effectively immutable after construction (<see langword="init"/>-only setters).
    /// Safe for concurrent reads from any thread.</para>
    ///
    /// <para><b>Cross-platform:</b> Fully portable.  No platform-specific behaviour.</para>
    /// </remarks>
    internal sealed class CloudflareDnsRecordRequest
    {
        /// <summary>DNS record type.  Always <c>"TXT"</c> for ACME DNS-01 challenges.</summary>
        public required string Type { get; init; }

        /// <summary>Fully-qualified record name (e.g. <c>_acme-challenge.example.com</c>).</summary>
        public required string Name { get; init; }

        /// <summary>TXT record value (the ACME DNS-01 challenge token digest).</summary>
        public required string Content { get; init; }

        /// <summary>Record TTL in seconds.</summary>
        public required int Ttl { get; init; }

        /// <summary>
        /// Gets a value indicating whether the record is proxied through Cloudflare.  Must be <see langword="false"/> for TXT.
        /// </summary>
        public bool Proxied { get; init; }
    }

}
