// <copyright file="ProcessorQueueSpoolWriterScalingPolicy.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: spool writer scaling policy evaluated about once per second by the writer hosted service.

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Default <see cref="ISpoolWriterScalingPolicy"/> that maps absolute spool queue backlog to a desired writer count
    /// using fixed-depth buckets, capped by local CPU count and a repository-wide hard ceiling.
    /// </summary>
    /// <remarks>
    /// <para><b>Role:</b> Registered as the default <see cref="ISpoolWriterScalingPolicy"/> implementation in
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>. Each scaling tick,
    /// <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/> forwards
    /// <see cref="NntpSpoolWriteQueue.Depth"/> and <see cref="NntpSpoolWriteQueue.Capacity"/> to
    /// <see cref="ComputeDesiredWriters"/>. <see cref="Hosting.NntpSpoolWriterHostedService"/> polls about once per
    /// second and passes the result to <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/>, which applies separate
    /// downscale hysteresis before removing workers.</para>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// Return <see cref="MinWriters"/> when queue depth is not positive, or when
    /// <see cref="MaxWriters"/> equals <see cref="MinWriters"/> (for example on single-processor hosts).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Compute <c>desired = CeilDiv(queueDepth, BacklogPerWriter)</c> as
    /// <c>(queueDepth + BacklogPerWriter - 1) / BacklogPerWriter</c> using 64-bit arithmetic.
    /// </description>
    /// </item>
    /// <item>
    /// <description>Clamp <c>desired</c> to the inclusive range from <see cref="MinWriters"/> through <see cref="MaxWriters"/>.</description>
    /// </item>
    /// </list>
    /// <para>
    /// Bucket size is fixed at <see cref="BacklogPerWriter"/> so scaling aggressiveness does not change when
    /// <see cref="Sockets.Configuration.NntpServerOptions.SpoolQueueCapacity"/> is tuned as a safety or memory limit.
    /// For example, with a backlog tier of 64 items: depths 1–64 request one writer, 65–128 request two, and
    /// depth 300 requests five writers regardless of whether capacity is 1024 or 100000.
    /// </para>
    /// <para>
    /// Maximum writers (<c>MaxWriters * BacklogPerWriter</c> queued items) is only reached when backlog exceeds that
    /// product. A full queue below that threshold therefore does not necessarily request every allowed worker.
    /// </para>
    /// <para>
    /// <b>Throughput validation:</b> Spool writer throughput is not linear in worker count (filesystem locks, metadata
    /// stores, and storage saturation). Treat <see cref="SpoolWriterHardCap"/> as a starting ceiling and validate with
    /// host benchmarks before raising it in production.
    /// </para>
    /// <para>
    /// <b>Backlog tier tuning:</b> Under sustained load, compare <c>nntp.spool.queue.depth</c>,
    /// <c>nntp.spool.writers.active</c>, <c>nntp.spool.write.success</c>, and
    /// <c>nntp.spool.payload.bytes_written</c> while sweeping <see cref="BacklogPerWriter"/> (for example 32, 48, 64,
    /// 96, 128). Lower values scale out sooner; higher values reduce worker churn. The default of 64 is a reasonable
    /// starting point for NVMe-backed hosts with relatively small articles but should be confirmed on target hardware.
    /// </para>
    /// <para><b>Threading:</b> Instances are registered as singletons and are safe to call from the hosted scaling loop
    /// without external synchronization. <see cref="MaxWriters"/> is evaluated once per instance at construction from
    /// <c>Environment.ProcessorCount</c>.</para>
    /// <example>
    /// <para>Representative depths with <see cref="BacklogPerWriter"/> = 64 and <see cref="MaxWriters"/> = 24:</para>
    /// <code>
    /// ComputeDesiredWriters(0, capacity)    // returns 1
    /// ComputeDesiredWriters(64, capacity)   // returns 1
    /// ComputeDesiredWriters(65, capacity)   // returns 2
    /// ComputeDesiredWriters(300, capacity)  // returns 5
    /// ComputeDesiredWriters(1536, capacity) // returns 24
    /// </code>
    /// <para>The second parameter is ignored by this implementation; capacity does not change any result above.</para>
    /// </example>
    /// </remarks>
    internal sealed class ProcessorQueueSpoolWriterScalingPolicy : ISpoolWriterScalingPolicy
    {
        /// <summary>
        /// Fixed backlog item count covered by each writer tier before requesting another worker.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Scaling uses absolute depth buckets of this size rather than dividing
        /// <see cref="Sockets.Configuration.NntpServerOptions.SpoolQueueCapacity"/> by <see cref="MaxWriters"/>, so
        /// queue capacity can be raised for headroom without slowing scale-out.
        /// </para>
        /// <para>
        /// The literal value is <c>64</c>. Validate on target hardware by comparing queue depth, active writer count,
        /// and write throughput while sweeping candidate values (for example 32, 48, 64, 96, 128). On high-core NVMe
        /// hosts with small typical article sizes, values between 32 and 64 often outperform larger tiers that delay
        /// scale-out.
        /// </para>
        /// <para>
        /// Re-benchmark when average per-item spool cost shifts materially (pre/post-processing mix, storage backend
        /// latency, or article size distribution).
        /// </para>
        /// </remarks>
        public const int BacklogPerWriter = 64;

        /// <summary>
        /// Repository-wide upper bound on spool writer workers regardless of host CPU count.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The literal value is <c>24</c>. <see cref="MaxWriters"/> on each policy instance is
        /// <c>Math.Min(Environment.ProcessorCount, SpoolWriterHardCap)</c>, so hosts with more than 24 logical
        /// processors still request at most 24 writers.
        /// </para>
        /// <para>
        /// Additional workers may not increase end-to-end spool throughput once storage or filesystem contention
        /// dominates; benchmark on target hardware before raising this constant.
        /// </para>
        /// </remarks>
        public const int SpoolWriterHardCap = 24;

        /// <summary>
        /// Minimum spool writer workers this policy returns and the pool should maintain after startup.
        /// </summary>
        /// <value>Always <c>1</c>.</value>
        /// <remarks>
        /// Satisfies <see cref="ISpoolWriterScalingPolicy.MinWriters"/>. The spool pipeline keeps at least one writer
        /// draining <see cref="NntpSpoolWriteQueue"/> even when backlog is empty so the first post-startup article does
        /// not wait for a scaling tick to create a worker.
        /// </remarks>
        public int MinWriters => 1;

        /// <summary>
        /// Maximum spool writer workers this policy returns for the lifetime of the instance.
        /// </summary>
        /// <value>
        /// <c>Math.Min(Environment.ProcessorCount, SpoolWriterHardCap)</c> captured when the instance is constructed.
        /// On a typical many-core host this is 24; on a single-processor host it is 1.
        /// </value>
        /// <remarks>
        /// <para>
        /// Satisfies <see cref="ISpoolWriterScalingPolicy.MaxWriters"/>. The value is frozen at construction so scaling
        /// ticks do not re-query <c>Environment.ProcessorCount</c>. Because the type is registered as a singleton, the
        /// captured processor count reflects the host at first service resolution.
        /// </para>
        /// <para>
        /// When this value equals <see cref="MinWriters"/>, <see cref="ComputeDesiredWriters"/> always returns
        /// <see cref="MinWriters"/> regardless of queue depth.
        /// </para>
        /// </remarks>
        public int MaxWriters { get; } = Math.Min(Environment.ProcessorCount, SpoolWriterHardCap);

        /// <summary>
        /// Computes the desired spool writer count from current queue depth using fixed backlog tiers.
        /// </summary>
        /// <param name="queueDepth">
        /// Current number of queued spool items. Callers typically pass <see cref="NntpSpoolWriteQueue.Depth"/>, which
        /// includes channel-backed items and items dequeued but not yet accounted by
        /// <see cref="NntpSpoolWriteQueue.NotifyDequeued"/>. Non-positive depth yields <see cref="MinWriters"/>.
        /// </param>
        /// <param name="queueCapacity">
        /// Configured maximum queue item count (typically <see cref="NntpSpoolWriteQueue.Capacity"/> from
        /// <see cref="Sockets.Configuration.NntpServerOptions.SpoolQueueCapacity"/>). Retained for
        /// <see cref="ISpoolWriterScalingPolicy"/> contract compatibility; this implementation does not read
        /// <paramref name="queueCapacity"/> when computing the target count. Supplied for API uniformity with callers
        /// such as <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/>.
        /// </param>
        /// <returns>
        /// Target worker count in the inclusive range <see cref="MinWriters"/> through <see cref="MaxWriters"/>. Uses
        /// ceiling division of <paramref name="queueDepth"/> by <see cref="BacklogPerWriter"/>, then clamps. Never
        /// throws for any numeric inputs.
        /// </returns>
        /// <remarks>
        /// <para><b>Early exits:</b></para>
        /// <list type="bullet">
        /// <item><description>Non-positive <paramref name="queueDepth"/> returns <see cref="MinWriters"/>.</description></item>
        /// <item><description>When <see cref="MaxWriters"/> is less than or equal to <see cref="MinWriters"/>, returns <see cref="MinWriters"/>.</description></item>
        /// </list>
        /// <para>
        /// Examples with <see cref="BacklogPerWriter"/> = 64 and <see cref="MaxWriters"/> = 24:
        /// </para>
        /// <list type="bullet">
        /// <item><description>Depth 0 returns 1 writer.</description></item>
        /// <item><description>Depth 11 returns 1 writer.</description></item>
        /// <item><description>Depth 64 returns 1 writer.</description></item>
        /// <item><description>Depth 65 returns 2 writers.</description></item>
        /// <item><description>Depth 300 returns 5 writers (unchanged whether capacity is 1024 or 8192).</description></item>
        /// <item><description>Depth 1536 returns 24 writers.</description></item>
        /// </list>
        /// <para>
        /// Pure calculation with no side effects. Downscale damping and worker lifecycle are owned by
        /// <see cref="NntpSpoolWriterPool.AdjustWriterCountAsync"/>.
        /// </para>
        /// </remarks>
        public int ComputeDesiredWriters(long queueDepth, int queueCapacity)
        {
            if (queueDepth <= 0)
            {
                return MinWriters;
            }

            int max = MaxWriters;
            if (max <= MinWriters)
            {
                return MinWriters;
            }

            long desired = (queueDepth + BacklogPerWriter - 1L) / BacklogPerWriter;
            return (int)Math.Clamp(desired, MinWriters, max);
        }
    }
}
