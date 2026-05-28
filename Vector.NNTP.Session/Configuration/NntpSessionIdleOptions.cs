// <copyright file="NntpSessionIdleOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Session.Configuration
{
    /// <summary>
    /// Idle timeout binding for session lease TTL (mirrors <c>NntpServer</c> host section).
    /// </summary>
    public sealed class NntpSessionIdleOptions
    {
        /// <summary>
        /// Configuration section name (<c>NntpServer</c>).
        /// </summary>
        public const string SectionName = "NntpServer";

        /// <summary>
        /// Gets or sets the idle timeout as ISO duration.
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Gets or sets optional idle timeout in seconds (wins over <see cref="IdleTimeout"/> when set).
        /// </summary>
        public int? IdleTimeoutSeconds { get; set; }
    }
}
