// <copyright file="RedisMultiplexerBackgroundScaler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// Hosted service that adds Redis multiplexers when coordination load signals scale-up.
    /// </summary>
    public sealed partial class RedisMultiplexerBackgroundScaler : BackgroundService
    {
        private readonly RedisMultiplexerPool _pool;
        private readonly IOptions<NntpSessionCoordinationOptions> _options;
        private readonly ILogger<RedisMultiplexerBackgroundScaler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisMultiplexerBackgroundScaler"/> class.
        /// </summary>
        /// <param name="pool">Pool to scale.</param>
        /// <param name="options">Coordination options.</param>
        /// <param name="logger">Logger.</param>
        public RedisMultiplexerBackgroundScaler(
            RedisMultiplexerPool pool,
            IOptions<NntpSessionCoordinationOptions> options,
            ILogger<RedisMultiplexerBackgroundScaler> logger)
        {
            ArgumentNullException.ThrowIfNull(pool);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);
            _pool = pool;
            _options = options;
            _logger = logger;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (bool scaleUpSignal in _pool.ScaleUpReader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!scaleUpSignal)
                {
                    continue;
                }

                try
                {
                    NntpSessionCoordinationOptions options = _options.Value;
                    int connectionCount = _pool.Snapshot.Count;
                    if (connectionCount >= options.MaxConnections)
                    {
                        continue;
                    }

                    _ = await _pool.AddMultiplexerAsync(stoppingToken).ConfigureAwait(false);
                    LogScaledUp(_logger, connectionCount + 1);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogScaleError(_logger, ex);
                }
            }
        }
    }
}
