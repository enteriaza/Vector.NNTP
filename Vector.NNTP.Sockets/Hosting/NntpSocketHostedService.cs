// <copyright file="NntpSocketHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: hosted service wrapping the accept loop.

namespace Vector.NNTP.Sockets.Hosting
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// <see cref="IHostedService"/> that runs the NNTP TCP accept loop.
    /// </summary>
    internal sealed partial class NntpSocketHostedService : BackgroundService
    {
        private readonly NntpSocketAcceptor _acceptor;
        private readonly NntpInFlightSessionTracker _inFlight;
        private readonly ILogger<NntpSocketHostedService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSocketHostedService"/> class.
        /// </summary>
        /// <param name="acceptor">Socket acceptor.</param>
        /// <param name="inFlight">In-flight session tracker.</param>
        /// <param name="logger">Logger.</param>
        public NntpSocketHostedService(
            NntpSocketAcceptor acceptor,
            NntpInFlightSessionTracker inFlight,
            ILogger<NntpSocketHostedService> logger)
        {
            this._acceptor = acceptor ?? throw new ArgumentNullException(nameof(acceptor));
            this._inFlight = inFlight ?? throw new ArgumentNullException(nameof(inFlight));
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            this.LogStarting();
            await this._acceptor.RunAsync(stoppingToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            await this._inFlight.DrainAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
