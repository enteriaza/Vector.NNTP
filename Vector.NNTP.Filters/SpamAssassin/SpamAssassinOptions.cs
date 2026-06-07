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
    /// <para>
    /// <b>Host list:</b> When <see cref="Hosts"/> is empty at validation time, <see cref="SpamAssassinOptionsValidator"/> copies
    /// <see cref="Host"/> into a single-element array for backward-compatible single-host JSON.
    /// </para>
    /// </remarks>
    public sealed class SpamAssassinOptions
    {
        /// <summary>
        /// Configuration section name (<c>SpamAssassin</c>).
        /// </summary>
        public const string SectionName = "SpamAssassin";

        /// <summary>
        /// Legacy single spamd hostname or IP address.
        /// </summary>
        /// <remarks>
        /// Normalized into <see cref="Hosts"/> when the array is empty at startup validation. When both are set,
        /// <see cref="Hosts"/> drives round-robin selection and <see cref="Host"/> is kept as the first entry for display.
        /// </remarks>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// Round-robin spamd hostnames or IP addresses used for every spamd command connection.
        /// </summary>
        /// <remarks>
        /// <see cref="SpamAssassin"/> selects the next host with thread-safe round-robin on each TCP connect (CHECK, PING,
        /// PROCESS, TELL, and other commands). When empty before validation, <see cref="Host"/> is copied in.
        /// </remarks>
        public string[] Hosts { get; set; } = [];

        /// <summary>
        /// spamd TCP port (default 783).
        /// </summary>
        [Range(1, 65535)]
        public int Port { get; set; } = 783;

        /// <summary>
        /// Protocol minor version sent as <c>SPAMC/{SpamdProtocolVersion}</c> (for example <c>1.5</c>).
        /// </summary>
        [RegularExpression(@"^\d+\.\d+$")]
        public string SpamdProtocolVersion { get; set; } = "1.5";

        /// <summary>
        /// Optional <c>User:</c> request header forwarded to spamd for per-user scores.
        /// </summary>
        /// <remarks>
        /// When null or empty, no <c>User:</c> header is sent. spamd may apply default scoring rules without it.
        /// </remarks>
        public string? User { get; set; }

        /// <summary>
        /// TCP connect timeout in milliseconds.
        /// </summary>
        [Range(100, 120_000)]
        public int ConnectTimeoutMilliseconds { get; set; } = 5_000;

        /// <summary>
        /// End-to-end operation timeout in milliseconds (connect, send, receive).
        /// </summary>
        /// <remarks>
        /// Applied to the linked cancellation token for each command and to <see cref="System.Net.Sockets.NetworkStream"/>
        /// read/write timeouts on the wire session.
        /// </remarks>
        [Range(100, 600_000)]
        public int OperationTimeoutMilliseconds { get; set; } = 120_000;
    }
}
