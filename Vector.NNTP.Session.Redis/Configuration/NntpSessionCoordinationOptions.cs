// <copyright file="NntpSessionCoordinationOptions.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using System.ComponentModel.DataAnnotations;

namespace Vector.NNTP.Session.Redis.Configuration
{
    /// <summary>
    /// Redis coordination settings bound from the host's top-level <c>Redis</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para><b>Binding:</b> Registered via <c>AddNntpSessionRedis</c> with <c>ValidateOnStart</c>. Production hosts
    /// must supply at least one entry in <see cref="Hosts"/>.</para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// {
    ///   "Redis": {
    ///     "Hosts": ["redis01a", "redis01b"],
    ///     "Port": 6379,
    ///     "Retry": 3,
    ///     "TimeoutSeconds": 3
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public sealed partial class NntpSessionCoordinationOptions
    {
        /// <summary>
        /// Configuration section name (existing operator JSON key).
        /// </summary>
        public const string SectionName = "Redis";

        /// <summary>Default Redis port.</summary>
        private const int DefaultRedisPort = 6379;

        /// <summary>
        /// Gets or sets one or more Redis hostnames or IP addresses.
        /// </summary>
        /// <remarks>
        /// Each entry must be a bare hostname or IP — no URI scheme or port suffix. The <see cref="Port"/> property
        /// applies to all endpoints uniformly. StackExchange.Redis uses the full list for failover within each pool
        /// multiplexer.
        /// </remarks>
        [Required(ErrorMessage = "Redis:Hosts is required.")]
        [MinLength(1, ErrorMessage = "Redis:Hosts must contain at least one host.")]
        public string[] Hosts { get; set; } = [];

        /// <summary>Gets or sets redis port shared by all endpoints in <see cref="Hosts"/>.</summary>
        [Range(1, 65_535, ErrorMessage = "Redis:Port must be between 1 and 65,535.")]
        public int Port { get; set; } = DefaultRedisPort;

        /// <summary>Gets or sets maps to StackExchange.Redis <c>ConnectRetry</c>.</summary>
        [Range(0, 100, ErrorMessage = "Redis:Retry must be between 0 and 100.")]
        public int Retry { get; set; } = 3;

        /// <summary>Gets or sets connect and synchronous command timeout in seconds.</summary>
        [Range(1, 300, ErrorMessage = "Redis:TimeoutSeconds must be between 1 and 300.")]
        public int TimeoutSeconds { get; set; } = 3;

        /// <summary>Gets or sets minimum live <see cref="StackExchange.Redis.ConnectionMultiplexer"/> instances at startup.</summary>
        [Range(1, 64, ErrorMessage = "Redis:MinConnections must be between 1 and 64.")]
        public int MinConnections { get; set; } = 1;

        /// <summary>Gets or sets maximum multiplexers the background scaler may open under load.</summary>
        [Range(1, 64, ErrorMessage = "Redis:MaxConnections must be between 1 and 64.")]
        public int MaxConnections { get; set; } = 4;

        /// <summary>Gets or sets base reconnect delay in milliseconds for per-host backoff.</summary>
        [Range(100, 60_000, ErrorMessage = "Redis:PoolReconnectBaseDelayMs must be between 100 and 60,000.")]
        public int PoolReconnectBaseDelayMs { get; set; } = 1000;

        /// <summary>Gets or sets maximum reconnect delay cap in milliseconds for per-host backoff.</summary>
        [Range(1000, 300_000, ErrorMessage = "Redis:PoolReconnectMaxDelayMs must be between 1,000 and 300,000.")]
        public int PoolReconnectMaxDelayMs { get; set; } = 30_000;

        /// <summary>
        /// Gets or sets a key prefix for all coordination keys.
        /// </summary>
        public string KeyPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the heartbeat interval in seconds.
        /// </summary>
        [Range(1, 3600)]
        public int HeartbeatIntervalSeconds { get; set; } = 60;

        /// <summary>
        /// Gets or sets the minimum lease TTL in seconds (floor for acquire/heartbeat).
        /// </summary>
        [Range(60, 86400)]
        public int TtlMinimumSeconds { get; set; } = 300;

        /// <summary>
        /// Gets or sets the multiplier applied to idle timeout when computing lease TTL.
        /// </summary>
        [Range(1, 10)]
        public double TtlIdleMultiplier { get; set; } = 2.0;

        /// <summary>
        /// Gets or sets the slow Redis call threshold in milliseconds (0 disables scale-up signals).
        /// </summary>
        [Range(0, 600_000)]
        public int SlowRedisCallThresholdMs { get; set; } = 5;

        /// <summary>
        /// Gets or sets bounded SCAN calls per reconciliation pass.
        /// </summary>
        [Range(1, 1000)]
        public int ReconciliationMaxScanCalls { get; set; } = 10;

        /// <summary>
        /// Gets or sets the SCAN COUNT hint per reconciliation call.
        /// </summary>
        [Range(1, 10_000)]
        public int ReconciliationScanCount { get; set; } = 100;

        /// <summary>
        /// Gets or sets periodic reconciliation sweep interval in seconds (0 disables hosted sweep).
        /// </summary>
        [Range(0, 86400)]
        public int ReconciliationIntervalSeconds { get; set; } = 300;
    }
}
