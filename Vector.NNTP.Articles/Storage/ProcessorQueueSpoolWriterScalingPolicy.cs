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
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// Return <see cref="MinWriters"/> when queue depth is not positive, or when
    /// <see cref="MaxWriters"/> equals <see cref="MinWriters"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Compute <c>desired = CeilDiv(queueDepth, BacklogPerWriter)</c> as
    /// <c>(queueDepth + BacklogPerWriter - 1) / BacklogPerWriter</c> using 64-bit arithmetic.
    /// </description>
    /// </item>
    /// <item>
    /// <description>Clamp <c>desired</c> to the inclusive range from <see cref="MinWriters"/> through <c>maxWriters</c>.</description>
    /// </item>
    /// </list>
    /// <para>
    /// Bucket size is fixed at <see cref="BacklogPerWriter"/> so scaling aggressiveness does not change when
    /// <see cref="Sockets.Configuration.NntpServerOptions.SpoolQueueCapacity"/> is tuned as a safety or memory limit.
    /// For example, with a backlog tier of 64 items: depths 1-64 request one writer, 65-128 request two, and
    /// depth 300 requests five writers regardless of whether capacity is 1024 or 100000.
    /// </para>
    /// <para>
    /// Maximum writers (<c>maxWriters * BacklogPerWriter</c> queued items) is only reached when backlog exceeds that
    /// product. A full queue below that threshold therefore does not necessarily request every allowed worker.
    /// </para>
    /// <para>
    /// This type only computes a target count. <see cref="Hosting.NntpSpoolWriterHostedService"/> applies three consecutive
    /// downscale ticks before removing workers so depth oscillation around bucket boundaries does not flap the pool.
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
    /// <para><b>Threading:</b> Instances are registered as singletons and are safe to call from the hosted scaling loop.</para>
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
        /// Default <c>64</c>. Validate on target hardware by comparing queue depth, active writer count, and write
        /// throughput while sweeping candidate values (for example 32, 48, 64, 96, 128). On high-core NVMe hosts with
        /// small typical article sizes, values between 32 and 64 often outperform larger tiers that delay scale-out.
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
        /// <see cref="MaxWriters"/> is computed as the lesser of <c>Environment.ProcessorCount</c> and this constant.
        /// Additional workers may not increase end-to-end spool throughput once storage or filesystem contention
        /// dominates; benchmark on target hardware before raising this value.
        /// </remarks>
        public const int SpoolWriterHardCap = 24;

        /// <summary>
        /// Cached per-instance maximum writer count computed once at construction from host CPU count and
        /// <see cref="SpoolWriterHardCap"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Evaluated as <c>Math.Min(Environment.ProcessorCount, SpoolWriterHardCap)</c> so scaling limits follow the
        /// host processor count without recomputing on every scaling tick. The literal constant name refers to
        /// <see cref="SpoolWriterHardCap"/>.
        /// </para>
        /// </remarks>
        private readonly int maxWriters = Math.Min(Environment.ProcessorCount, SpoolWriterHardCap);

        /// <summary>
        /// Gets the minimum number of spool writer workers the pool should maintain.
        /// </summary>
        /// <remarks>
        /// Always 1. The spool pipeline keeps at least one writer draining
        /// <see cref="NntpSpoolWriteQueue"/> even when backlog is empty.
        /// </remarks>
        public int MinWriters => 1;

        /// <summary>
        /// Gets the maximum number of spool writer workers this policy will request.
        /// </summary>
        /// <remarks>
        /// Returns the value captured in <c>maxWriters</c> at instance construction. Hosts with more than
        /// <see cref="SpoolWriterHardCap"/> logical processors still cap at 24 writers.
        /// </remarks>
        public int MaxWriters => this.maxWriters;

        /// <summary>
        /// Computes the desired spool writer count from current queue depth and configured capacity.
        /// </summary>
        /// <param name="queueDepth">
        /// Current number of queued spool items (typically <see cref="NntpSpoolWriteQueue.Depth"/>). Non-positive depth
        /// yields <see cref="MinWriters"/>.
        /// </param>
        /// <param name="queueCapacity">
        /// Configured maximum queue item count (typically <c>NntpServerOptions.SpoolQueueCapacity</c>). Retained for
        /// <see cref="ISpoolWriterScalingPolicy"/> contract compatibility; this implementation ignores capacity when
        /// computing the target count. Supplied for API uniformity with callers such as
        /// <see cref="NntpSpoolWriterPool.ComputeDesiredWriterCount"/>.
        /// </param>
        /// <returns>
        /// An integer in the inclusive range <see cref="MinWriters"/> through <see cref="MaxWriters"/> derived from
        /// backlog buckets. Never throws; non-positive depth conservatively returns <see cref="MinWriters"/>.
        /// </returns>
        /// <remarks>
        /// <para><b>Example (backlog tier 64 items, <see cref="MaxWriters"/> 24):</b></para>
        /// <list type="bullet">
        /// <item><description>Depth 0 returns 1 writer.</description></item>
        /// <item><description>Depth 11 returns 1 writer.</description></item>
        /// <item><description>Depth 64 returns 1 writer.</description></item>
        /// <item><description>Depth 65 returns 2 writers.</description></item>
        /// <item><description>Depth 300 returns 5 writers (same at capacity 1024 or 8192).</description></item>
        /// <item><description>Depth 1536 returns 24 writers when <see cref="MaxWriters"/> is 24.</description></item>
        /// </list>
        /// </remarks>
        public int ComputeDesiredWriters(long queueDepth, int queueCapacity)
        {
            if (queueDepth <= 0)
            {
                return this.MinWriters;
            }

            int max = this.maxWriters;
            if (max <= this.MinWriters)
            {
                return this.MinWriters;
            }

            long desired = (queueDepth + BacklogPerWriter - 1L) / BacklogPerWriter;
            return (int)Math.Clamp(desired, this.MinWriters, max);
        }
    }
}
