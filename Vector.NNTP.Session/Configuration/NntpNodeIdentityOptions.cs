// <copyright file="NntpNodeIdentityOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Session.Configuration
{
    /// <summary>
    /// Stable cluster node identity bound from the host <c>NntpServer</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="NodeName"/> is a stable cluster identity (not a display label). Changing it leaves orphaned
    /// Redis keys under the previous <c>node:{oldName}:*</c> prefix until manually cleaned up.
    /// </para>
    /// </remarks>
    public sealed class NntpNodeIdentityOptions
    {
        /// <summary>
        /// Configuration section name (shared with socket and encryption options).
        /// </summary>
        public const string SectionName = "NntpServer";

        /// <summary>
        /// Gets or sets the stable node identifier for this host instance (for example <c>nntpd01</c>).
        /// </summary>
        [Required(ErrorMessage = "NntpServer:NodeName is required for Redis session coordination.")]
        public string NodeName { get; set; } = string.Empty;
    }
}
