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
    /// Initializes a new instance of the <see cref="NntpSocketHostedService"/> class.
    /// </remarks>
    /// <param name="acceptor">Socket acceptor.</param>
    /// <param name="inFlight">In-flight session tracker.</param>
    /// <param name="logger">Logger.</param>
    internal sealed partial class NntpSocketHostedService(
        NntpSocketAcceptor acceptor,
        NntpInFlightSessionTracker inFlight,
        ILogger<NntpSocketHostedService> logger) : BackgroundService
    {
        private readonly NntpSocketAcceptor _acceptor = acceptor ?? throw new ArgumentNullException(nameof(acceptor));
        private readonly NntpInFlightSessionTracker _inFlight = inFlight ?? throw new ArgumentNullException(nameof(inFlight));
        private readonly ILogger<NntpSocketHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LogStarting();
            await _acceptor.RunAsync(stoppingToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            await _inFlight.DrainAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
