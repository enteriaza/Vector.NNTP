// <copyright file="NntpSpoolWriterHostedService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: spool writer pool startup, one-second scaling loop, and graceful shutdown.

using Microsoft.Extensions.Hosting;
using Vector.NNTP.Articles.Storage;

namespace Vector.NNTP.Articles.Hosting
{
    /// <summary>
    /// <see cref="BackgroundService"/> that owns transit spool writer pool startup, periodic scaling, and shutdown drain.
    /// </summary>
    /// <remarks>
    /// <para><b>Registration:</b> Registered as a hosted service by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/> alongside the
    /// <see cref="NntpSpoolWriterPool"/> singleton it manages.</para>
    /// <para><b>Execute loop:</b></para>
    /// <list type="number">
    /// <item><description><see cref="NntpSpoolWriterPool.StartAsync"/> — starts the pool once and ensures at least one worker.</description></item>
    /// <item><description>Every second — obtains the pool's desired writer count via
    /// <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/> (which consults queue depth and the injected
    /// <see cref="ISpoolWriterScalingPolicy"/>).</description></item>
    /// <item><description>Forwards that count to <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/>, which applies
    /// internal three-tick downscale hysteresis before removing workers.</description></item>
    /// </list>
    /// <para>
    /// <b>Shutdown:</b> <see cref="StopAsync"/> delegates to <see cref="NntpSpoolWriterPool.StopAsync"/>, which completes
    /// queue admission, cancels workers, and awaits pump tasks. Host application shutdown also cancels the token
    /// supplied to <see cref="ExecuteAsync"/>, ending the periodic timer loop.
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> The pool is single-use per host instance; this service does not restart workers after stop.
    /// </para>
    /// <para><b>Threading:</b> The scaling loop runs on the hosted-service thread pool task; pool mutations occur under
    /// the pool's internal gate.</para>
    /// </remarks>
    internal sealed class NntpSpoolWriterHostedService : BackgroundService
    {
        /// <summary>
        /// Singleton writer pool whose lifecycle and scaling this service drives.
        /// </summary>
        /// <remarks>
        /// Injected at construction and shared with socket threads through
        /// <see cref="NntpSpoolWriteQueue"/> and <see cref="NntpSpoolTransitStorage"/>.
        /// </remarks>
        private readonly NntpSpoolWriterPool _writerPool;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolWriterHostedService"/> class.
        /// </summary>
        /// <param name="writerPool">
        /// Writer pool singleton registered by
        /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="writerPool"/> is <see langword="null"/>.</exception>
        public NntpSpoolWriterHostedService(NntpSpoolWriterPool writerPool)
        {
            ArgumentNullException.ThrowIfNull(writerPool);
            _writerPool = writerPool;
        }

        /// <summary>
        /// Drains the writer pool during host shutdown.
        /// </summary>
        /// <param name="cancellationToken">
        /// Host shutdown token forwarded to <see cref="NntpSpoolWriterPool.StopAsync"/> for awaiting worker task completion.
        /// </param>
        /// <returns>
        /// A task that completes after the pool completes queue admission, cancels all workers, and awaits pump tasks.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Delegates entirely to <see cref="NntpSpoolWriterPool.StopAsync"/>; does not reset pool startup state, so a
        /// subsequent <see cref="NntpSpoolWriterPool.StartAsync"/> on the same instance remains a no-op.
        /// </para>
        /// <para>
        /// Worker <see cref="OperationCanceledException"/> outcomes during drain are swallowed inside the pool; host
        /// shutdown cancellation on <paramref name="cancellationToken"/> propagates from
        /// <see cref="NntpSpoolWriterPool.StopAsync"/> when awaiting workers.
        /// </para>
        /// </remarks>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            return _writerPool.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Starts the writer pool and runs the one-second scaling loop until host shutdown.
        /// </summary>
        /// <param name="stoppingToken">
        /// Host lifetime token; canceled when the application stops, which ends
        /// <see cref="PeriodicTimer.WaitForNextTickAsync(CancellationToken)"/> waits.
        /// </param>
        /// <returns>
        /// A task that runs for the hosted-service lifetime and completes when <paramref name="stoppingToken"/> is
        /// canceled.
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// Propagated when <paramref name="stoppingToken"/> is canceled during
        /// <see cref="PeriodicTimer.WaitForNextTickAsync(CancellationToken)"/> or when
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> awaits scale-down worker completion during shutdown.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Propagated when <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/> returns a value outside scaling
        /// policy bounds (unexpected for a correctly configured policy).
        /// </exception>
        /// <remarks>
        /// <para><b>Startup:</b> <see cref="NntpSpoolWriterPool.StartAsync"/> is awaited once before the timer loop.
        /// Repeat calls are not made; pool startup is single-use per instance.</para>
        /// <para><b>Scaling interval:</b> A <see cref="PeriodicTimer"/> with a one-second period drives
        /// <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/> and
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> on each tick. The service does not consult
        /// <see cref="ISpoolWriterScalingPolicy"/> directly — the pool owns that relationship.</para>
        /// <para>
        /// Uncaught exceptions from pool methods fault the hosted-service task and should be treated as configuration or
        /// implementation defects rather than expected runtime outcomes.
        /// </para>
        /// </remarks>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _writerPool.StartAsync(stoppingToken).ConfigureAwait(false);

            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                int desired = _writerPool.ComputeDesiredWriterCount();
                await _writerPool.AdjustWriterCountAsync(desired, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
