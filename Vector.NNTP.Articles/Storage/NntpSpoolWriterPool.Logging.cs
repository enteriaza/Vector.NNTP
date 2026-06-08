// <copyright file="NntpSpoolWriterPool.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 700-719 (spool writer pool scaling diagnostics).

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> logging partial for
    /// <see cref="NntpSpoolWriterPool"/> worker-pool scaling transitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Keeps pool lifecycle and locking code in <c>NntpSpoolWriterPool.cs</c> while centralizing EventId
    /// assignments, log levels, and message templates here. Each helper is a <c>private static partial</c> method
    /// expanded at compile time by the logging source generator; callers pass the pool instance's
    /// <c>ILogger&lt;NntpSpoolWriterPool&gt;</c> as <see cref="ILogger"/>.
    /// </para>
    /// <para>
    /// These methods record active writer count changes only. Steady-state dequeue, per-article preprocess/postprocess/write
    /// failures, and history repair diagnostics are logged by <see cref="NntpSpoolWriterPump"/> (EventIds 1-7), not here.
    /// Queue depth and writer gauges are also published through <see cref="Metrics.NntpSpoolMetrics"/> without a matching
    /// log line on every scaling tick.
    /// </para>
    /// <para>
    /// <b>Invocation context:</b> <see cref="SpoolWriterPoolScaled"/> is called from
    /// <see cref="AdjustWriterCountAsync"/> after worker list mutations complete under <see cref="_gate"/> and, on
    /// scale-down, after canceled worker tasks have been awaited. It is
    /// not called when the desired count equals the active count or when scale-down hysteresis has not yet reached three
    /// consecutive observations.
    /// </para>
    /// <para>
    /// <b>EventId bands (Articles spool):</b> worker failures 1-9 (<c>NntpSpoolWriterPump.Logging.cs</c>), queue
    /// management 10-19 (reserved), scaling 700-719 (this partial), shutdown 720-729 (reserved). Assign new pool scaling
    /// diagnostics within 700-719 before extending the band.
    /// </para>
    /// <para><b>EventIds defined in this partial:</b></para>
    /// <list type="table">
    /// <listheader><term>EventId</term><description>Meaning</description></listheader>
    /// <item><term>700</term><description>Active writer count changed after scale-up or hysteresis-qualified scale-down — <see cref="LogLevel.Information"/>.</description></item>
    /// <item><term>701-719</term><description>Reserved; unassigned in this repository revision.</description></item>
    /// </list>
    /// <para><b>Threading:</b> Static helpers have no mutable state. <see cref="SpoolWriterPoolScaled"/> is invoked from
    /// the hosted scaling loop thread after releasing <see cref="_gate"/> and any scale-down awaits;
    /// it is safe to call without additional synchronization.</para>
    /// </remarks>
    internal sealed partial class NntpSpoolWriterPool
    {
        /// <summary>
        /// Logs an active writer count change after the pool applies a scale-up or hysteresis-qualified scale-down.
        /// </summary>
        /// <param name="logger">
        /// Pool category logger (the <see cref="NntpSpoolWriterPool"/> instance field passed from
        /// <see cref="AdjustWriterCountAsync"/>).
        /// </param>
        /// <param name="previousCount">
        /// Active <see cref="NntpSpoolWriterPump"/> worker tasks recorded in the pool worker list immediately before the
        /// adjustment under <see cref="_gate"/>.
        /// </param>
        /// <param name="newCount">
        /// Active worker tasks in the pool list after workers were added (<see cref="AddWorkersUnsafe"/>) or removed from
        /// the list (<see cref="RemoveWorkersUnsafe"/>). On scale-down this reflects the post-removal count before logging
        /// even though canceled workers are awaited first.
        /// </param>
        /// <param name="queueDepth">
        /// Current <see cref="NntpSpoolWriteQueue.Depth"/> sampled at log time after scale-down awaits complete. Includes
        /// channel-backed items and items dequeued but not yet accounted by
        /// <see cref="NntpSpoolWriteQueue.NotifyDequeued"/> (in-flight preprocess, postprocess, or I/O).
        /// </param>
        /// <param name="queueCapacity">
        /// Configured maximum item count from <see cref="NntpSpoolWriteQueue.Capacity"/>
        /// (<see cref="Sockets.Configuration.NntpServerOptions.SpoolQueueCapacity"/>). Included for operator context in
        /// the <c>depth/capacity</c> suffix; the default <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> scales from
        /// absolute depth tiers and does not use capacity when computing desired writers.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="AdjustWriterCountAsync"/> only when <paramref name="newCount"/> differs from
        /// <paramref name="previousCount"/>. The call happens outside <see cref="_gate"/> so logging
        /// does not block worker list mutations under <see cref="_gate"/> or peer scaling ticks.
        /// </para>
        /// <para><b>Scale-up:</b> Occurs immediately when
        /// <see cref="ISpoolWriterScalingPolicy.ComputeDesiredWriters"/> (via
        /// <see cref="ComputeDesiredWriterCount"/>) requests more workers than are active. Resets downscale hysteresis.
        /// New <see cref="Task.Run(Func{Task}, CancellationToken)"/> workers begin asynchronously; this log does not wait
        /// for their first dequeue.</para>
        /// <para><b>Scale-down:</b> Requires three consecutive <see cref="AdjustWriterCountAsync"/> observations (typically
        /// one-second ticks from <see cref="Hosting.NntpSpoolWriterHostedService"/>) where desired count stays below the
        /// active count. Removed workers are canceled and awaited before this log is emitted.</para>
        /// <para>
        /// Emitted at <see cref="LogLevel.Information"/> with message template
        /// <c>Spool writer pool scaled from {PreviousCount} to {NewCount} (depth {QueueDepth}/{QueueCapacity}).</c>
        /// Suppressed on ticks where desired count is unchanged or hysteresis has not yet qualified a scale-down, avoiding
        /// noise on every one-second timer poll.
        /// </para>
        /// <para>
        /// Startup's initial single worker activated by <see cref="StartAsync"/> does not emit this event; only subsequent
        /// adjustments from <see cref="AdjustWriterCountAsync"/> do.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 700,
            Level = LogLevel.Information,
            Message = "Spool writer pool scaled from {PreviousCount} to {NewCount} (depth {QueueDepth}/{QueueCapacity}).")]
        private static partial void SpoolWriterPoolScaled(
            ILogger logger,
            int previousCount,
            int newCount,
            long queueDepth,
            int queueCapacity);
    }
}
