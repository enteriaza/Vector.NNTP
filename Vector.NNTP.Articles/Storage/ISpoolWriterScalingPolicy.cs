// <copyright file="ISpoolWriterScalingPolicy.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: pluggable spool writer scaling contract evaluated about once per second by the writer hosted service.

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Pluggable policy that maps spool queue pressure signals to a desired <see cref="NntpSpoolWriterPump"/> worker
    /// count for <see cref="NntpSpoolWriterPool"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Decouples backlog-to-worker algorithms from pool lifecycle. Each scaling tick,
    /// <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/> samples <see cref="NntpSpoolWriteQueue.Depth"/> and
    /// <see cref="NntpSpoolWriteQueue.Capacity"/> and forwards them to <see cref="ComputeDesiredWriters"/>.
    /// <see cref="Hosting.NntpSpoolWriterHostedService"/> polls about once per second, passes the result to
    /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/>, and the pool applies separate three-tick downscale
    /// hysteresis before removing workers.
    /// </para>
    /// <para>
    /// Policies compute targets only; they do not start, stop, cancel, or await worker <see cref="Task"/> instances.
    /// Scale-up is immediate when the desired count exceeds the active count; scale-down waits for three consecutive
    /// observations where desired stays below active. Active count changes may emit Information-level EventId 700 through
    /// the pool logging partial.
    /// </para>
    /// <para>
    /// <b>Default implementation:</b> <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> is registered as the
    /// singleton implementation by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>. Replace the
    /// <see cref="ISpoolWriterScalingPolicy"/> registration in DI to supply a custom algorithm (for example
    /// occupancy-based scaling using <c>queueCapacity</c>).
    /// </para>
    /// <para>
    /// <b>Contract:</b> <see cref="ComputeDesiredWriters"/> must return a value in the inclusive range
    /// <see cref="MinWriters"/> through <see cref="MaxWriters"/> for all inputs.
    /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> throws <see cref="ArgumentOutOfRangeException"/> when
    /// the desired count is outside those bounds. Implementations should treat non-positive queue depth as minimum load and
    /// return <see cref="MinWriters"/>.
    /// </para>
    /// <para><b>Threading:</b> Implementations are typically singletons and must be safe to call from the hosted
    /// scaling loop without external synchronization. They must not block on I/O or worker completion.</para>
    /// </remarks>
    public interface ISpoolWriterScalingPolicy
    {
        /// <summary>
        /// Gets the minimum number of spool writer workers the pool should maintain after startup.
        /// </summary>
        /// <value>
        /// Lower bound for <see cref="ComputeDesiredWriters"/> results and for
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> validation. Must be positive in production
        /// configurations.
        /// </value>
        /// <remarks>
        /// <para>
        /// <see cref="NntpSpoolWriterPool.StartAsync"/> always activates at least one worker under pool lock regardless of
        /// backlog. <see cref="ComputeDesiredWriters"/> should return this value when queue depth is not positive so the
        /// pool keeps a baseline drainer even on an empty queue.
        /// </para>
        /// <para>
        /// The default <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> returns <c>1</c>.
        /// </para>
        /// </remarks>
        public int MinWriters { get; }

        /// <summary>
        /// Gets the maximum number of spool writer workers this policy will request.
        /// </summary>
        /// <value>
        /// Upper bound for <see cref="ComputeDesiredWriters"/> results and for
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> validation. Must be greater than or equal to
        /// <see cref="MinWriters"/>.
        /// </value>
        /// <remarks>
        /// <para>
        /// Caps scale-out even when backlog continues to grow. When this value equals <see cref="MinWriters"/>,
        /// <see cref="ComputeDesiredWriters"/> should always return <see cref="MinWriters"/> regardless of depth (for
        /// example on single-processor hosts using the default policy).
        /// </para>
        /// <para>
        /// The default <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> sets this to
        /// <c>Math.Min(Environment.ProcessorCount, ProcessorQueueSpoolWriterScalingPolicy.SpoolWriterHardCap)</c> once
        /// at construction.
        /// </para>
        /// </remarks>
        public int MaxWriters { get; }

        /// <summary>
        /// Computes the desired spool writer worker count from current queue depth and configured capacity.
        /// </summary>
        /// <param name="queueDepth">
        /// Current queued item count. Callers pass <see cref="NntpSpoolWriteQueue.Depth"/>, which includes
        /// channel-backed items and items dequeued but not yet accounted by
        /// <see cref="NntpSpoolWriteQueue.NotifyDequeued"/> (in-flight preprocess, postprocess, or I/O). May be briefly
        /// stale relative to concurrent enqueue activity; implementations must tolerate races without throwing.
        /// </param>
        /// <param name="queueCapacity">
        /// Configured maximum queued item count. Callers pass <see cref="NntpSpoolWriteQueue.Capacity"/> (from
        /// <see cref="Sockets.Configuration.NntpServerOptions.SpoolQueueCapacity"/>). Implementations may use capacity
        /// for occupancy-based scaling (<c>depth / capacity</c>) or ignore it when scaling from absolute depth only.
        /// The default <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> ignores
        /// <paramref name="queueCapacity"/> entirely.
        /// </param>
        /// <returns>
        /// Target worker count in the inclusive range <see cref="MinWriters"/> through <see cref="MaxWriters"/>. Should
        /// return <see cref="MinWriters"/> for non-positive <paramref name="queueDepth"/>. Must never throw for numeric
        /// inputs; invalid policy outputs that fall outside <see cref="MinWriters"/>..<see cref="MaxWriters"/> cause
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> to fault the scaling loop.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Pure calculation hook with no side effects. Does not mutate <see cref="NntpSpoolWriteQueue"/>,
        /// <see cref="Metrics.NntpSpoolMetrics"/>, or worker tasks. Downscale damping, worker cancellation, and logging
        /// are owned by <see cref="NntpSpoolWriterPool"/>.
        /// </para>
        /// <para>
        /// <b>Default algorithm:</b> <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> uses fixed backlog tiers of
        /// <see cref="ProcessorQueueSpoolWriterScalingPolicy.BacklogPerWriter"/> items per writer (ceiling division of
        /// depth, then clamp). Representative depths with default constants: depth 0 → 1 writer; depth 64 → 1; depth 65
        /// → 2; depth 300 → 5; depth 1536 → 24 on a many-core host.
        /// </para>
        /// <para>
        /// <b>Custom policies:</b> May incorporate <paramref name="queueCapacity"/> for percentage occupancy, byte-queue
        /// pressure (not available on this interface today), or host-specific ceilings. Document whether capacity affects
        /// results so operators tuning <see cref="Sockets.Configuration.NntpServerOptions.SpoolQueueCapacity"/> understand
        /// scaling behavior.
        /// </para>
        /// </remarks>
        public int ComputeDesiredWriters(long queueDepth, int queueCapacity);
    }
}
