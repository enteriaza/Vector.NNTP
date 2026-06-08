// <copyright file="NntpServerOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: configuration binding and startup validation.

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Sockets.Configuration
{
    /// <summary>
    /// TCP listener, idle timeout, TLS, compression, and authentication policy for the NNTP socket server.
    /// </summary>
    public sealed class NntpServerOptions
    {
        /// <summary>
        /// Configuration section name for <see cref="NntpServerOptions"/>.
        /// </summary>
        public const string SectionName = "NntpServer";

        /// <summary>
        /// Gets or sets the IPv4 bind address for cleartext and implicit-TLS listeners.
        /// </summary>
        /// <remarks>
        /// Empty or <c>*</c> binds all IPv4 interfaces (<c>0.0.0.0</c>). A separate IPv6 listener is started only when
        /// <see cref="BindAddress6"/> is configured; this property does not enable dual-stack acceptance on its own.
        /// </remarks>
        public string BindAddress { get; set; } = "0.0.0.0";

        /// <summary>
        /// Gets or sets the IPv6 bind address for cleartext and implicit-TLS listeners.
        /// </summary>
        /// <remarks>
        /// When empty, no IPv6 listener is started. <c>*</c> or <c>::</c> binds all IPv6 interfaces. When set to a
        /// specific address, <see cref="Hosting.NntpSocketAcceptor"/> starts a separate <see cref="System.Net.Sockets.TcpListener"/>
        /// on this address for each configured port (<see cref="Port"/>, <see cref="TlsPort"/>).
        /// </remarks>
        public string BindAddress6 { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the cleartext NNTP port (default 119).
        /// </summary>
        [Range(1, 65535)]
        public int Port { get; set; } = 119;

        /// <summary>
        /// Gets or sets the implicit TLS port (0 disables).
        /// </summary>
        [Range(0, 65535)]
        public int TlsPort { get; set; }

        /// <summary>
        /// Gets or sets the maximum concurrent connections (0 = unlimited).
        /// </summary>
        [Range(0, int.MaxValue)]
        public int MaxConnections { get; set; }

        /// <summary>
        /// Gets or sets the maximum concurrent connections per client IP address (0 = unlimited).
        /// </summary>
        [Range(0, int.MaxValue)]
        public int MaxConnectionsPerClientIp { get; set; }

        /// <summary>
        /// Gets or sets the per-read idle timeout in seconds before the server closes the connection.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int IdleTimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// Gets or sets a value indicating whether STARTTLS is advertised and permitted.
        /// </summary>
        public bool EnableStartTls { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether COMPRESS DEFLATE is advertised when supported.
        /// </summary>
        public bool EnableCompressDeflate { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether AUTHINFO USER/PASS and SASL require a TLS-protected connection first.
        /// </summary>
        public bool RequireTlsForAuthInfo { get; set; }

        /// <summary>
        /// Gets or sets the stable cluster node identifier for this host (for example <c>nntpd01</c>).
        /// </summary>
        /// <remarks>
        /// Must remain stable across restarts; changing it orphans Redis keys under the previous node prefix.
        /// </remarks>
        [Required]
        public string NodeName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the DNS domain suffix for this node (for example <c>usenetninja.net</c>).
        /// </summary>
        /// <remarks>
        /// Combined with <see cref="NodeName"/> to form the server FQDN used when synthesizing SpamAssassin
        /// <c>Received:</c> and <c>To:</c> headers on the transit spool path. When empty, <see cref="NodeName"/> alone
        /// is used.
        /// </remarks>
        public string DomainName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the server identification string embedded in the initial greeting and HELP text.
        /// </summary>
        [Required]
        public string ServerIdentification { get; set; } = "VectorNNTPD";

        /// <summary>
        /// Gets or sets a value indicating whether HAProxy PROXY protocol preambles are accepted on accept.
        /// </summary>
        public bool EnableProxyProtocol { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether PROXY preambles are only accepted from trusted first-hop sources.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When enabled, receiving a PROXY preamble from an untrusted peer is treated as a protocol error and the
        /// connection is closed without applying any of the claimed client endpoint data.
        /// </para>
        /// </remarks>
        public bool ProxyProtocolStrictTrustedSourcesOnly { get; set; } = true;

        /// <summary>
        /// Gets or sets the trusted first-hop sources allowed to send PROXY preambles.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Entries may be literal IP addresses (for example <c>192.0.2.10</c>) or CIDR ranges (for example
        /// <c>192.0.2.0/24</c> or <c>2001:db8::/32</c>).
        /// </para>
        /// </remarks>
        public string[] ProxyProtocolTrustedSources { get; set; } = [];

        /// <summary>
        /// Gets or sets trusted transit peer definitions for NNTPD peering (address matching, DNS refresh, cluster caps).
        /// </summary>
        public NntpTransitPeersOptions TransitPeers { get; set; } = new();

        /// <summary>
        /// Gets or sets the maximum decoded dot-stuffed article body size in bytes (0 disables the limit).
        /// </summary>
        [Range(0, long.MaxValue)]
        public long MaxArtSize { get; set; } = 1_048_576;

        /// <summary>
        /// Gets or sets the <see cref="StreamPipeReader"/> buffer size for socket sessions.
        /// </summary>
        [Range(4096, 16_777_216)]
        public int PipeReadBufferBytes { get; set; } = 65_536;

        /// <summary>
        /// Gets or sets a value indicating whether CPU overload connection rejection is enabled.
        /// </summary>
        public bool CpuRejectEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the effective CPU EWMA percent at or above which new connections and commands are rejected.
        /// </summary>
        [Range(1, 100)]
        public double CpuRejectThresholdPercent { get; set; } = 80;

        /// <summary>
        /// Gets or sets the effective CPU EWMA percent at or below which accepting resumes (hysteresis).
        /// </summary>
        [Range(1, 100)]
        public double CpuResumeThresholdPercent { get; set; } = 75;

        /// <summary>
        /// Gets or sets the CPU sampling interval in seconds for the overload gate.
        /// </summary>
        [Range(1, 3600)]
        public int CpuSamplingIntervalSeconds { get; set; } = 1;

        /// <summary>
        /// Gets or sets a value indicating whether process CPU utilization contributes to the gate.
        /// </summary>
        public bool CpuRejectUseProcess { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether host-wide CPU utilization contributes to the gate.
        /// </summary>
        public bool CpuRejectUseHost { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether cgroup quota-relative CPU utilization contributes when available.
        /// </summary>
        public bool CpuRejectUseCgroup { get; set; } = true;

        /// <summary>
        /// Gets or sets the transit article spool root directory (empty → <c>{AppContext.BaseDirectory}/Spool</c>).
        /// </summary>
        public string SpoolDir { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the maximum in-flight spool queue item count.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each queued item holds a full <c>byte[]</c> copy of the article. Enqueue is rejected when either this
        /// item cap or <see cref="MaxQueuedBytes"/> is exceeded — whichever trips first yields
        /// <see cref="Storage.NntpTransitStorageResult.QueueFull"/>.
        /// </para>
        /// <para>
        /// Example: <c>1024</c> items with <c>MaxArtSize = 4 MiB</c> and <c>MaxQueuedBytes = 1 GiB</c> binds on
        /// bytes (~256 max-sized articles) before the item cap.
        /// </para>
        /// </remarks>
        [Range(1, int.MaxValue)]
        public int SpoolQueueCapacity { get; set; } = 1024;

        /// <summary>
        /// Gets or sets the maximum sum of queued article payload bytes across the spool write queue.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Default <c>1_073_741_824</c> (1 GiB). Tune independently of <see cref="SpoolQueueCapacity"/> so many small
        /// articles and fewer large articles are not treated identically.
        /// </para>
        /// <para>
        /// Worst-case RAM is bounded by <c>min(SpoolQueueCapacity × MaxArtSize, MaxQueuedBytes)</c> plus object overhead.
        /// </para>
        /// </remarks>
        [Range(1, long.MaxValue)]
        public long MaxQueuedBytes { get; set; } = 1_073_741_824;

        /// <summary>
        /// Gets or sets the path token prepended to <c>Path:</c> headers during spool preprocessing (transit hosts).
        /// </summary>
        /// <remarks>
        /// <para>Empty or whitespace skips mutation. Transit NNTPD hosts should set a stable hop token.</para>
        /// </remarks>
        public string PathAppend { get; set; } = string.Empty;
    }

    /// <summary>
    /// Validates <see cref="NntpServerOptions"/> at startup.
    /// </summary>
    public sealed class NntpServerOptionsValidator : IValidateOptions<NntpServerOptions>
    {
        /// <summary>
        /// Validates required identity fields, idle timeout, pipe buffer size, CPU hysteresis, and transit peer options.
        /// </summary>
        /// <param name="name">Options name (unused).</param>
        /// <param name="options">Bound server options.</param>
        /// <returns><see cref="ValidateOptionsResult.Success"/> or a failure describing the first violated constraint.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public ValidateOptionsResult Validate(string? name, NntpServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return string.IsNullOrWhiteSpace(options.NodeName)
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.NodeName)} is required.")
                : string.IsNullOrWhiteSpace(options.ServerIdentification)
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.ServerIdentification)} is required.")
                : options.IdleTimeoutSeconds <= 0
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.IdleTimeoutSeconds)} must be positive.")
                : options.PipeReadBufferBytes < 4096
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.PipeReadBufferBytes)} must be at least 4096.")
                : options.CpuResumeThresholdPercent >= options.CpuRejectThresholdPercent
                ? ValidateOptionsResult.Fail(
                    $"{nameof(NntpServerOptions.CpuResumeThresholdPercent)} must be less than {nameof(NntpServerOptions.CpuRejectThresholdPercent)}.")
                : !options.CpuRejectUseProcess && !options.CpuRejectUseHost && !options.CpuRejectUseCgroup
                ? ValidateOptionsResult.Fail(
                    "At least one of CpuRejectUseProcess, CpuRejectUseHost, or CpuRejectUseCgroup must be true.")
                : options.SpoolQueueCapacity <= 0
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.SpoolQueueCapacity)} must be positive.")
                : options.MaxQueuedBytes <= 0
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.MaxQueuedBytes)} must be positive.")
                : NntpBindAddressNormalizer.ValidateBindAddress(options.BindAddress)
                ?? NntpBindAddressNormalizer.ValidateBindAddress6(options.BindAddress6)
                ?? NntpTransitPeersOptionsValidator.Validate(options.TransitPeers);
        }
    }
}
