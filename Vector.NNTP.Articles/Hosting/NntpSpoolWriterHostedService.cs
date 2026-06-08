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
    /// <para>
    /// <b>Role:</b> Bridges the generic host lifetime to <see cref="NntpSpoolWriterPool"/>. Socket threads enqueue articles
    /// through <see cref="NntpSpoolTransitStorage"/>; this service ensures at least one
    /// <see cref="NntpSpoolWriterPump"/> worker is running at startup and periodically reconciles active worker count with
    /// queue depth via <see cref="ISpoolWriterScalingPolicy"/>. It does not preprocess, postprocess, or write articles
    /// itself.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/> together with sibling
    /// hosted services (one-shot spool configuration log and <see cref="NntpSpoolThroughputLogHostedService"/> minute
    /// throughput snapshots). Shares the
    /// <see cref="NntpSpoolWriterPool"/> singleton injected into this constructor.
    /// </para>
    /// <para><b>Execute loop:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriterPool.StartAsync"/> — invoked once with the BackgroundService stopping token; ensures at
    /// least one worker under the pool gate (see pool remarks; does not emit scaling EventId 700).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="PeriodicTimer"/> with a one-second period — the first tick fires after the initial one-second wait, not
    /// immediately at startup.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// On each tick — <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/> samples
    /// <see cref="NntpSpoolWriteQueue.Depth"/> and forwards depth and capacity to the injected
    /// <see cref="ISpoolWriterScalingPolicy"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> — scale-up is immediate; scale-down requires three
    /// consecutive ticks where desired count is below active count (pool-internal hysteresis). Worker-count changes emit
    /// pool EventId 700 and update gauge <c>nntp.spool.writers.active</c>.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Shutdown:</b> The overridden <see cref="StopAsync"/> delegates to <see cref="NntpSpoolWriterPool.StopAsync"/>
    /// with the host shutdown token. That completes queue admission via <see cref="NntpSpoolWriteQueue.Complete"/>,
    /// cancels all pump workers, and awaits worker tasks. It does not call <c>BackgroundService.StopAsync</c> base
    /// behavior; writer drain is intentional shutdown surface for the transit spool. The
    /// <see cref="ExecuteAsync"/> scaling loop exits when the BackgroundService stopping token is canceled (typically
    /// ending waits in <see cref="PeriodicTimer.WaitForNextTickAsync"/>).
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> <see cref="NntpSpoolWriterPool"/> instances are single-use per host process;
    /// <see cref="NntpSpoolWriterPool.StopAsync"/> does not reset the pool startup latch, so a later
    /// <see cref="NntpSpoolWriterPool.StartAsync"/> on the same instance remains a no-op.
    /// </para>
    /// <para>
    /// <b>Threading:</b> The scaling loop runs on the BackgroundService execute task; pool worker list mutations occur
    /// under the pool's internal gate. <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> releases the gate before
    /// awaiting canceled workers.
    /// </para>
    /// </remarks>
    internal sealed class NntpSpoolWriterHostedService : BackgroundService
    {
        /// <summary>
        /// Singleton writer pool whose lifecycle and periodic scaling this service drives.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Injected at construction. The same instance is shared with <see cref="NntpSpoolWriteQueue"/>,
        /// <see cref="NntpSpoolWriterPump"/>, and <see cref="NntpSpoolTransitStorage"/> registration in
        /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
        /// </para>
        /// <para>Only this hosted service invokes <see cref="NntpSpoolWriterPool.StartAsync"/> and the overridden
        /// <see cref="StopAsync"/> on the pool.</para>
        /// </remarks>
        private readonly NntpSpoolWriterPool _writerPool;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolWriterHostedService"/> class.
        /// </summary>
        /// <param name="writerPool">
        /// Writer pool singleton registered by
        /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>. Must be the same
        /// instance resolved for <see cref="NntpSpoolWriterPump"/> and <see cref="NntpSpoolWriteQueue"/> consumers.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="writerPool"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Does not start workers; <see cref="ExecuteAsync"/> calls <see cref="NntpSpoolWriterPool.StartAsync"/> after
        /// the host starts this service.
        /// </remarks>
        public NntpSpoolWriterHostedService(NntpSpoolWriterPool writerPool)
        {
            ArgumentNullException.ThrowIfNull(writerPool);
            _writerPool = writerPool;
        }

        /// <summary>
        /// Drains the writer pool during host shutdown.
        /// </summary>
        /// <param name="cancellationToken">
        /// Host shutdown token forwarded to <see cref="NntpSpoolWriterPool.StopAsync"/> for awaiting worker task
        /// completion. When canceled during drain, the pool abandons remaining worker awaits without resetting
        /// <see cref="NntpSpoolWriterPool"/> startup state.
        /// </param>
        /// <returns>
        /// A task that completes after the pool calls <see cref="NntpSpoolWriteQueue.Complete"/>, cancels all workers,
        /// and awaits pump tasks (or host cancellation aborts the awaits).
        /// </returns>
        /// <remarks>
        /// <para>
        /// Overrides <see cref="BackgroundService.StopAsync"/> to delegate entirely to
        /// <see cref="NntpSpoolWriterPool.StopAsync"/> rather than invoking the base implementation. Graceful transit
        /// spool shutdown is defined by pool drain (queue complete + worker cancel), not by this type's scaling loop
        /// alone.
        /// </para>
        /// <para>
        /// Worker <see cref="OperationCanceledException"/> outcomes during drain are swallowed inside the pool.
        /// <see cref="OperationCanceledException"/> on <paramref name="cancellationToken"/> propagates from worker
        /// <see cref="Task.WaitAsync(CancellationToken)"/> awaits inside <see cref="NntpSpoolWriterPool.StopAsync"/>.
        /// </para>
        /// <para>
        /// Does not emit pool EventId 700; shutdown sets active writer count to zero without a scaling log line.
        /// </para>
        /// </remarks>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            return _writerPool.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Starts the writer pool and runs the one-second scaling loop until the BackgroundService stopping token fires.
        /// </summary>
        /// <param name="stoppingToken">
        /// BackgroundService lifetime token linked into <see cref="NntpSpoolWriterPool.StartAsync"/> and forwarded to
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> on each scaling tick. Cancellation ends waits in
        /// <see cref="PeriodicTimer.WaitForNextTickAsync"/> and may propagate from scale-down worker awaits during host
        /// stop.
        /// </param>
        /// <returns>
        /// A task that runs for the hosted-service lifetime. Completes when <paramref name="stoppingToken"/> is canceled
        /// and the timer loop exits (typically via <see cref="OperationCanceledException"/> from
        /// <see cref="PeriodicTimer.WaitForNextTickAsync"/>).
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// Propagated when <paramref name="stoppingToken"/> is canceled during
        /// <see cref="PeriodicTimer.WaitForNextTickAsync"/> or when
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> awaits scale-down worker completion while
        /// <paramref name="stoppingToken"/> is in the canceled state.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Propagated from <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> when
        /// <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/> returns a value outside
        /// <see cref="ISpoolWriterScalingPolicy.MinWriters"/>..<see cref="ISpoolWriterScalingPolicy.MaxWriters"/>
        /// (unexpected for a correctly configured policy).
        /// </exception>
        /// <remarks>
        /// <para>
        /// <b>Startup:</b> <see cref="NntpSpoolWriterPool.StartAsync"/> is awaited once before the timer loop. The pool
        /// startup latch ensures repeat calls are no-ops for the process lifetime.
        /// </para>
        /// <para>
        /// <b>Scaling interval:</b> Each timer tick samples queue depth and calls
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/>. This service does not consult
        /// <see cref="ISpoolWriterScalingPolicy"/> directly — the pool owns policy injection and validation.
        /// </para>
        /// <para>
        /// <b>Faults:</b> Uncaught exceptions from pool methods fault the BackgroundService execute task and should be
        /// treated as configuration or implementation defects, not expected load outcomes. Per-article pump failures are
        /// logged inside <see cref="NntpSpoolWriterPump"/> and do not stop this loop.
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
