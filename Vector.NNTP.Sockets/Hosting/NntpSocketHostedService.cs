// <copyright file="NntpSocketHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: hosted service wrapping the accept loop.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vector.NNTP.Sockets.Hosting
{
    /// <summary>
    /// <see cref="IHostedService"/> that runs the NNTP TCP accept loop.
    /// </summary>
    /// <remarks>
    /// Drains in-flight sessions during <see cref="StopAsync"/> after the accept loop stops.
    /// </remarks>
    /// <param name="acceptor">Socket acceptor.</param>
    /// <param name="inFlight">In-flight session tracker.</param>
    /// <param name="logger">Logger.</param>
    internal sealed partial class NntpSocketHostedService(
        NntpSocketAcceptor acceptor,
        NntpInFlightSessionTracker inFlight,
        ILogger<NntpSocketHostedService> logger) : BackgroundService
    {
        /// <summary>
        /// TCP acceptor running cleartext and TLS listener loops.
        /// </summary>
        private readonly NntpSocketAcceptor _acceptor = acceptor ?? throw new ArgumentNullException(nameof(acceptor));

        /// <summary>
        /// Tracker used to drain active sessions during host shutdown.
        /// </summary>
        private readonly NntpInFlightSessionTracker _inFlight = inFlight ?? throw new ArgumentNullException(nameof(inFlight));

        /// <summary>
        /// Logger for hosted service lifecycle events.
        /// </summary>
        private readonly ILogger<NntpSocketHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Runs the TCP accept loop until host shutdown is requested.
        /// </summary>
        /// <param name="stoppingToken">Token signaled when the host is stopping.</param>
        /// <returns>A task that runs until <paramref name="stoppingToken"/> is canceled.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LogStarting();
            await _acceptor.RunAsync(stoppingToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the accept loop and waits for in-flight sessions to drain.
        /// </summary>
        /// <param name="cancellationToken">Host shutdown cancellation token.</param>
        /// <returns>A task that completes when the accept loop stops and in-flight sessions drain.</returns>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            await _inFlight.DrainAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
