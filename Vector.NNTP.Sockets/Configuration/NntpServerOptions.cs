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
        /// Gets or sets the bind address for cleartext NNTP (empty or <c>*</c> for all interfaces).
        /// </summary>
        public string BindAddress { get; set; } = "0.0.0.0";

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
        /// Gets or sets optional idle timeout in seconds from <c>idleTimeoutSeconds</c> JSON key.
        /// </summary>
        public int? IdleTimeoutSeconds { get; set; }

        /// <summary>
        /// Gets or sets the idle timeout before the server closes the connection.
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

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
        public string[] ProxyProtocolTrustedSources { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets trusted transit peer definitions for NNTPD peering (address matching, DNS refresh, cluster caps).
        /// </summary>
        public NntpTransitPeersOptions TransitPeers { get; set; } = new();
    }

    /// <summary>
    /// Validates <see cref="NntpServerOptions"/> at startup.
    /// </summary>
    public sealed class NntpServerOptionsValidator : IValidateOptions<NntpServerOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, NntpServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return string.IsNullOrWhiteSpace(options.NodeName)
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.NodeName)} is required.")
                : string.IsNullOrWhiteSpace(options.ServerIdentification)
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.ServerIdentification)} is required.")
                : options.IdleTimeoutSeconds is <= 0
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.IdleTimeoutSeconds)} must be positive when set.")
                : options.IdleTimeout <= TimeSpan.Zero
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.IdleTimeout)} must be positive.")
                : NntpTransitPeersOptionsValidator.Validate(options.TransitPeers);
        }
    }
}
