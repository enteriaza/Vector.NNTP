// <copyright file="NntpSessionIdleOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.ComponentModel.DataAnnotations;

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
        /// Gets or sets the per-read idle timeout in seconds (same value as socket enforcement).
        /// </summary>
        [Range(1, int.MaxValue)]
        public int IdleTimeoutSeconds { get; set; } = 600;
    }
}
