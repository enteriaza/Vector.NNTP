// <copyright file="NntpSpoolWriterPool.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 700-719 (spool writer pool scaling diagnostics).

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="NntpSpoolWriterPool"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These methods record operational transitions for the spool writer worker pool. Steady-state dequeue and per-article
    /// failures are logged by <see cref="NntpSpoolWriterPump"/> (EventIds 1-5), not here.
    /// </para>
    /// <para>
    /// <b>EventId bands (Articles spool):</b> scaling diagnostics use 700-719 in this partial; worker failures use 1-9 in
    /// <c>NntpSpoolWriterPump.Logging.cs</c>.
    /// </para>
    /// </remarks>
    internal sealed partial class NntpSpoolWriterPool
    {
        /// <summary>
        /// Logs an active writer count change after the pool applies a scale-up or scale-down decision.
        /// </summary>
        /// <param name="logger">Pool category logger passed from <see cref="NntpSpoolWriterPool"/>.</param>
        /// <param name="previousCount">Active <see cref="NntpSpoolWriterPump"/> worker tasks immediately before the adjustment.</param>
        /// <param name="newCount">Active worker tasks after workers were added or awaited cancellation.</param>
        /// <param name="queueDepth">
        /// Current <see cref="NntpSpoolWriteQueue.Depth"/> at log time (items still in channel including in-flight work
        /// not yet <see cref="NntpSpoolWriteQueue.NotifyDequeued"/>).
        /// </param>
        /// <param name="queueCapacity">
        /// Configured maximum item count for <see cref="NntpSpoolWriteQueue"/> (included for operator context; the default
        /// <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> scales from absolute depth and does not use capacity).
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="AdjustWriterCountAsync"/> only when <paramref name="newCount"/> differs from
        /// <paramref name="previousCount"/>. Scale-up is immediate when
        /// <see cref="ISpoolWriterScalingPolicy.ComputeDesiredWriters"/> requests more workers; scale-down requires
        /// three consecutive pool observations (typically one-second ticks from
        /// <see cref="Hosting.NntpSpoolWriterHostedService"/>) where desired count stays below the active count.
        /// </para>
        /// <para>
        /// Emitted at <see cref="LogLevel.Information"/> to support capacity planning without logging on every one-second
        /// timer tick when the writer count is unchanged.
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
