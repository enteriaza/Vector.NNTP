// <copyright file="RedisMultiplexerFactory.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// RedisMultiplexerFactory.cs -- Creates ConnectionMultiplexer instances from NntpSessionCoordinationOptions.
using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Factory that connects a <see cref="IConnectionMultiplexer"/> with all configured Redis endpoints.
    /// </summary>
    /// <remarks>
    /// Builds StackExchange.Redis <see cref="ConfigurationOptions"/> from validated
    /// <see cref="NntpSessionCoordinationOptions"/> and fails fast when the multiplexer reports disconnected.
    /// </remarks>
    /// <param name="logger">Logger for connect start and success events.</param>
    public sealed partial class RedisMultiplexerFactory(ILogger<RedisMultiplexerFactory> logger)
    {
        /// <summary>
        /// Logger for multiplexer connect lifecycle events.
        /// </summary>
        private readonly ILogger<RedisMultiplexerFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Connects asynchronously using all hosts in <paramref name="options"/>.
        /// </summary>
        /// <param name="options">Validated coordination options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Connected multiplexer.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public async Task<IConnectionMultiplexer> ConnectAsync(
            NntpSessionCoordinationOptions options,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(options);
            cancellationToken.ThrowIfCancellationRequested();
            ConfigurationOptions configuration = BuildConfigurationOptions(options);
            LogConnecting(_logger, options.Hosts.Length, options.Port, options.Retry, options.TimeoutSeconds);
            IConnectionMultiplexer multiplexer = await ConnectionMultiplexer.ConnectAsync(configuration)
                .ConfigureAwait(false);
            if (!multiplexer.IsConnected)
            {
                await multiplexer.DisposeAsync().ConfigureAwait(false);
                throw new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    "Redis multiplexer connected but reports no active endpoints.");
            }

            LogConnected(_logger, options.Hosts.Length, options.Port);
            return multiplexer;
        }

        /// <summary>
        /// Builds StackExchange.Redis configuration from coordination options.
        /// </summary>
        /// <param name="options">Validated options snapshot.</param>
        /// <returns>Configuration passed to StackExchange.Redis <see cref="ConnectionMultiplexer.ConnectAsync(ConfigurationOptions, System.IO.TextWriter?)"/>.</returns>
        internal static ConfigurationOptions BuildConfigurationOptions(NntpSessionCoordinationOptions options)
        {
            int timeoutMs = options.TimeoutSeconds * 1000;
            ConfigurationOptions configuration = new()
            {
                ConnectRetry = options.Retry,
                ConnectTimeout = timeoutMs,
                SyncTimeout = timeoutMs,
                AbortOnConnectFail = false,
            };
            for (int i = 0; i < options.Hosts.Length; i++)
            {
                configuration.EndPoints.Add(options.Hosts[i], options.Port);
            }

            return configuration;
        }
    }
}
