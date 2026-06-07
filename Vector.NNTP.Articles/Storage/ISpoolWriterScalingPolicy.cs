// <copyright file="ISpoolWriterScalingPolicy.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: pluggable spool writer scaling contract evaluated about once per second by the writer hosted service.

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Pluggable policy that maps spool queue pressure signals to a desired
    /// <see cref="NntpSpoolWriterPool"/> worker count.
    /// </summary>
    /// <remarks>
    /// <para><b>Role:</b> Implementations translate backlog observations into a target worker count.
    /// <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/> supplies
    /// <see cref="NntpSpoolWriteQueue.Depth"/> and <see cref="NntpSpoolWriteQueue.Capacity"/>;
    /// <see cref="Hosting.NntpSpoolWriterHostedService"/> polls about once per second and forwards the result to
    /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/>, which applies separate downscale hysteresis.</para>
    /// <para>
    /// Policies compute targets only; they do not start, stop, or await worker tasks. Custom implementations may be
    /// registered in DI instead of <see cref="ProcessorQueueSpoolWriterScalingPolicy"/>.
    /// </para>
    /// <para>
    /// <b>Contract:</b> <see cref="ComputeDesiredWriters"/> must return a value in the inclusive range
    /// <see cref="MinWriters"/> through <see cref="MaxWriters"/>. <see cref="NntpSpoolWriterPool"/> validates this
    /// range before adjusting workers.
    /// </para>
    /// <para><b>Threading:</b> Implementations are typically singletons and must be safe to call from the hosted
    /// scaling loop without external synchronization.</para>
    /// </remarks>
    public interface ISpoolWriterScalingPolicy
    {
        /// <summary>
        /// Gets the minimum number of spool writer workers the pool should maintain.
        /// </summary>
        /// <remarks>
        /// <see cref="NntpSpoolWriterPool"/> keeps at least this many workers after <see cref="NntpSpoolWriterPool.StartAsync"/>
        /// even when backlog is empty. The default <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> returns
        /// <c>1</c>.
        /// </remarks>
        int MinWriters { get; }

        /// <summary>
        /// Gets the maximum number of spool writer workers this policy will request.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Upper bound for <see cref="ComputeDesiredWriters"/> results and for
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/> validation.
        /// </para>
        /// <para>
        /// The default <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> caps at
        /// <c>Math.Min(Environment.ProcessorCount, ProcessorQueueSpoolWriterScalingPolicy.SpoolWriterHardCap)</c>.
        /// </para>
        /// </remarks>
        int MaxWriters { get; }

        /// <summary>
        /// Computes the desired spool writer worker count from current queue depth and configured capacity.
        /// </summary>
        /// <param name="queueDepth">
        /// Current queued item count. Callers typically pass <see cref="NntpSpoolWriteQueue.Depth"/>, which includes
        /// channel-backed items and items dequeued but not yet accounted by
        /// <see cref="NntpSpoolWriteQueue.NotifyDequeued"/>. May be briefly stale relative to concurrent enqueue
        /// activity; policies should tolerate that.
        /// </param>
        /// <param name="queueCapacity">
        /// Configured maximum queued item count. Callers typically pass <see cref="NntpSpoolWriteQueue.Capacity"/>
        /// (from <see cref="Sockets.Configuration.NntpServerOptions.SpoolQueueCapacity"/>). Implementations may use
        /// capacity for occupancy-based scaling or ignore it when scaling from absolute depth only; see
        /// <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> for the default depth-only behavior.
        /// </param>
        /// <returns>
        /// Target worker count in the inclusive range <see cref="MinWriters"/> through <see cref="MaxWriters"/>.
        /// Implementations should return <see cref="MinWriters"/> for non-positive depth; capacity handling is
        /// implementation-defined.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Pure calculation hook with no side effects. Does not mutate the queue or worker pool. Downscale damping and
        /// worker lifecycle are owned by <see cref="NntpSpoolWriterPool"/>.
        /// </para>
        /// <para>
        /// See <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> for the default fixed-tier backlog algorithm and
        /// examples at representative depths.
        /// </para>
        /// </remarks>
        int ComputeDesiredWriters(long queueDepth, int queueCapacity);
    }
}
