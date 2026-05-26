// <copyright file="Worker.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// Worker.cs -- Placeholder hosted service until the NNRP reader protocol loop is wired.

namespace Vector.NNTP.NNRPD
{
    /// <summary>
    /// Background service placeholder for the NNRPD worker host.
    /// </summary>
    /// <remarks>
    /// <para>Emits a periodic heartbeat until the reader server hosted service replaces this stub.</para>
    /// <para><b>Logging:</b> <see cref="LoggerMessageAttribute"/> partial methods are defined in
    /// <c>Worker.Logging.cs</c>.</para>
    /// </remarks>
    public sealed partial class Worker : BackgroundService
    {
        /// <summary>Logger for heartbeat diagnostics.</summary>
        private readonly ILogger<Worker> _logger;

        /// <summary>Initializes a new instance of the <see cref="Worker"/> class.</summary>
        /// <param name="logger">Logger for heartbeat events.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
        public Worker(ILogger<Worker> logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            _logger = logger;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                LogHeartbeat(DateTimeOffset.Now);
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
