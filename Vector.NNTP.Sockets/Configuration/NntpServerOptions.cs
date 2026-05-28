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
        /// Gets or sets the server identification string embedded in the initial greeting and HELP text.
        /// </summary>
        [Required]
        public string ServerIdentification { get; set; } = "VectorNNTPD";

        /// <summary>
        /// Gets or sets a value indicating whether HAProxy PROXY protocol preambles are accepted on accept.
        /// </summary>
        public bool EnableProxyProtocol { get; set; }
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
            return string.IsNullOrWhiteSpace(options.ServerIdentification)
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.ServerIdentification)} is required.")
                : options.IdleTimeoutSeconds is <= 0
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.IdleTimeoutSeconds)} must be positive when set.")
                : options.IdleTimeout <= TimeSpan.Zero
                ? ValidateOptionsResult.Fail($"{nameof(NntpServerOptions.IdleTimeout)} must be positive.")
                : ValidateOptionsResult.Success;
        }
    }
}
