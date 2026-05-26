// <copyright file="SpamAssassinOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: configuration for spamd TCP client endpoints and timeouts.
// SpamAssassinOptions.cs -- Host, port, and protocol options for <see cref="SpamAssassin"/>.

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Filters.SpamAssassin
{
    /// <summary>
    /// TCP and protocol settings for the SpamAssassin <c>spamd</c> daemon.
    /// </summary>
    /// <remarks>
    /// <para><b>Binding:</b> Configuration section <see cref="SectionName"/> (for example <c>SpamAssassin:Host</c>).</para>
    /// <para><b>Default port:</b> 783 is the traditional spamd port.</para>
    /// </remarks>
    public sealed class SpamAssassinOptions
    {
        /// <summary>Configuration section name (<c>SpamAssassin</c>).</summary>
        public const string SectionName = "SpamAssassin";

        /// <summary>spamd hostname or IP address.</summary>
        [Required]
        [MinLength(1)]
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>spamd TCP port (default 783).</summary>
        [Range(1, 65535)]
        public int Port { get; set; } = 783;

        /// <summary>
        /// Protocol minor version sent as <c>SPAMC/{SpamdProtocolVersion}</c> (for example <c>1.5</c>).
        /// </summary>
        [RegularExpression(@"^\d+\.\d+$")]
        public string SpamdProtocolVersion { get; set; } = "1.5";

        /// <summary>Optional <c>User:</c> request header forwarded to spamd for per-user scores.</summary>
        public string? User { get; set; }

        /// <summary>TCP connect timeout in milliseconds.</summary>
        [Range(100, 120_000)]
        public int ConnectTimeoutMilliseconds { get; set; } = 5_000;

        /// <summary>End-to-end operation timeout in milliseconds (connect, send, receive).</summary>
        [Range(100, 600_000)]
        public int OperationTimeoutMilliseconds { get; set; } = 120_000;
    }
}
