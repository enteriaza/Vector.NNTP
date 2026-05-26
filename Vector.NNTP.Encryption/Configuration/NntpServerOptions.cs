// <copyright file="NntpServerOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// NntpServerOptions.cs -- Minimal NNTP server identity bound from the host NntpServer configuration section.

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Encryption.Configuration
{
    /// <summary>
    /// NNTP server identity options consumed by the encryption library for node-scoped logging and future cluster
    /// coordination. Bound from the <c>NntpServer</c> section by the host; the library never reads JSON directly.
    /// </summary>
    public sealed class NntpServerOptions
    {
        /// <summary>
        /// Configuration section name used by hosts when binding these options.
        /// </summary>
        public const string SectionName = "NntpServer";

        /// <summary>
        /// Stable node identifier for this host instance (for example <c>nnrpd01</c> or <c>nntpd01</c>).
        /// </summary>
        [Required]
        public string NodeName { get; set; } = string.Empty;
    }
}
