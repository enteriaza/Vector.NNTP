// <copyright file="NntpUsersOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: MySQL-backed NNTP user store connection options.

using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Auth.MySql
{
    /// <summary>
    /// Configuration options for the MySQL-backed NNTP user and policy store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Binding:</b> This options type binds from the <see cref="SectionName"/> configuration section. Hosts should
    /// call <c>services.AddOptions&lt;NntpUsersOptions&gt;().Bind(configurationSection).ValidateDataAnnotations().ValidateOnStart()</c>
    /// to ensure that misconfiguration fails fast during startup instead of deferring errors to the first authentication attempt.
    /// </para>
    /// <para>
    /// <b>Thread safety:</b> Options instances are treated as immutable snapshots after validation. They are safe to read
    /// concurrently from multiple threads without additional locking.
    /// </para>
    /// </remarks>
    public sealed class NntpUsersOptions
    {
        /// <summary>
        /// Configuration section name for NNTP user storage.
        /// </summary>
        public const string SectionName = "NntpUsers";

        /// <summary>
        /// Gets or sets the MySQL connection string used to query the <c>nntpusers</c> table.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Format:</b> This string is passed directly to <c>MySqlConnector.MySqlConnection</c> and should follow the
        /// standard MySQL ADO.NET connection string format, including pooling and timeout settings appropriate for the host.
        /// </para>
        /// </remarks>
        [Required]
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the command timeout, in seconds, for user lookups.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Scope:</b> This timeout applies to the single user-lookup query issued during authentication. It should be
        /// short enough to bound client wait time under backend issues but long enough to tolerate normal database latency.
        /// </para>
        /// <para>
        /// <b>Range:</b> Values less than 1 or greater than 600 seconds are rejected by options validation.
        /// </para>
        /// </remarks>
        [Range(1, 600)]
        public int CommandTimeout { get; set; } = 5;
    }
}
