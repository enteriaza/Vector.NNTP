// <copyright file="NntpSpoolWriteQueue.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: bounded in-memory transit spool queue with item and byte-budget backpressure.

using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Bounded in-memory spool queue that applies item-count and queued-byte admission limits before writing to an
    /// underlying <see cref="Channel{T}"/> consumed by spool writer workers.
    /// </summary>
    /// <remarks>
    /// <para><b>Role:</b> Transit producers (for example <see cref="NntpSpoolTransitStorage"/>) call
    /// <see cref="TryEnqueue"/> on the hot path. <see cref="NntpSpoolWriterPump"/> workers dequeue through
    /// <see cref="Reader"/> and release accounting through <see cref="NotifyDequeued(int)"/> after each item is
    /// processed.</para>
    /// <para><b>Admission:</b> Under <see cref="_sync"/>, <see cref="TryEnqueue"/> rejects when either
    /// <see cref="Depth"/> would reach <see cref="Capacity"/> or queued payload bytes would exceed
    /// <see cref="MaxQueuedBytes"/>. Successful enqueues increment <see cref="_depth"/> and <see cref="_queuedBytes"/>
    /// in the same critical section as <see cref="ChannelWriter{T}.TryWrite"/>.</para>
    /// <para>
    /// <b>Consumer contract:</b> Only spool writer pump workers may dequeue. Multiple workers may read
    /// <see cref="Reader"/> concurrently because the channel is created with <c>SingleReader = false</c>, but every
    /// successful read must be paired with exactly one <see cref="NotifyDequeued(int)"/> after processing completes.
    /// Additional dequeue paths would desynchronize counters from channel contents.
    /// </para>
    /// <para>
    /// <b>Channel full mode:</b> <see cref="BoundedChannelFullMode.DropWrite"/> is defensive. Admission is governed by
    /// <see cref="_depth"/> and <see cref="_queuedBytes"/>; the channel should not become full independently while
    /// counters are accurate. After <see cref="Complete"/>, <see cref="TryEnqueue"/> may observe <c>TryWrite</c>
    /// failures even when counters show capacity — callers treat that as rejection.
    /// </para>
    /// <para>
    /// <b>Observed depth:</b> <see cref="Depth"/> and <see cref="QueuedBytes"/> include items still in the channel and
    /// items already dequeued but not yet <see cref="NotifyDequeued"/> (in-flight writer work). Volatile reads may not
    /// reflect a single instant snapshot when observed separately; scaling and metrics tolerate that.
    /// </para>
    /// <para>
    /// <b>Metrics:</b> Enqueue, reject, and dequeue paths update <see cref="NntpSpoolMetrics"/> so operators can
    /// observe backlog and admission pressure.
    /// </para>
    /// <para><b>Threading:</b> Registered as a singleton. <see cref="TryEnqueue"/> and <see cref="NotifyDequeued"/> are
    /// safe under concurrent producers and multiple pump workers. <see cref="Depth"/> and <see cref="QueuedBytes"/> are
    /// lock-free observer reads.</para>
    /// <para><b>Lifecycle:</b> <see cref="NntpSpoolWriterPool"/> calls <see cref="Complete"/> during shutdown so
    /// workers drain remaining items before cancellation.</para>
    /// </remarks>
    internal sealed class NntpSpoolWriteQueue
    {
        /// <summary>
        /// Serializes counter updates, enqueue admission checks, and dequeue accounting.
        /// </summary>
        /// <remarks>
        /// All mutations to <see cref="_depth"/> and <see cref="_queuedBytes"/> occur while holding this lock.
        /// </remarks>
        private readonly object _sync = new();

        /// <summary>
        /// Channel writer used for non-blocking enqueue attempts after admission succeeds.
        /// </summary>
        /// <remarks>
        /// Completed exactly once via <see cref="Complete"/> during host shutdown. Callers must not complete this
        /// writer directly.
        /// </remarks>
        private readonly ChannelWriter<NntpSpoolWriteItem> _writer;

        /// <summary>
        /// Maximum queued item count copied from <see cref="NntpServerOptions.SpoolQueueCapacity"/> at construction.
        /// </summary>
        private readonly int _capacity;

        /// <summary>
        /// Maximum total queued payload bytes copied from <see cref="NntpServerOptions.MaxQueuedBytes"/> at construction.
        /// </summary>
        /// <remarks>
        /// Production values are typically in the gigabyte to tens-of-gigabytes range. Admission compares using
        /// subtraction rather than addition to avoid <see cref="long"/> overflow on pathological counter values.
        /// </remarks>
        private readonly long _maxQueuedBytes;

        /// <summary>
        /// Spool metrics recorder invoked on enqueue success, enqueue rejection, and dequeue accounting.
        /// </summary>
        private readonly NntpSpoolMetrics _metrics;

        /// <summary>
        /// Current queued item count updated under <see cref="_sync"/> on successful
        /// <see cref="TryEnqueue"/> / <see cref="NotifyDequeued(int)"/> pairs.
        /// </summary>
        /// <remarks>
        /// Exposed to readers through <see cref="Depth"/>, which loads the value without acquiring <see cref="_sync"/>.
        /// </remarks>
        private long _depth;

        /// <summary>
        /// Current sum of queued article payload bytes updated under <see cref="_sync"/> on successful enqueue/dequeue pairs.
        /// </summary>
        /// <remarks>
        /// Exposed to readers through <see cref="QueuedBytes"/>, which loads the value without acquiring <see cref="_sync"/>.
        /// </remarks>
        private long _queuedBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolWriteQueue"/> class.
        /// </summary>
        /// <param name="options">Bound server options supplying <see cref="NntpServerOptions.SpoolQueueCapacity"/> and
        /// <see cref="NntpServerOptions.MaxQueuedBytes"/>.</param>
        /// <param name="metrics">Metrics recorder updated on enqueue, reject, and dequeue paths.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="options"/> or <paramref name="metrics"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <see cref="NntpServerOptions.SpoolQueueCapacity"/> or
        /// <see cref="NntpServerOptions.MaxQueuedBytes"/> is not positive.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Creates a bounded <see cref="Channel{NntpSpoolWriteItem}"/> with capacity <see cref="Capacity"/>,
        /// <see cref="BoundedChannelFullMode.DropWrite"/>, and both <c>SingleReader</c> and <c>SingleWriter</c> set to
        /// <see langword="false"/> so multiple transit sessions and pump workers can enqueue and dequeue concurrently.
        /// <c>AllowSynchronousContinuations</c> is disabled to avoid producer/consumer continuation coupling on the hot
        /// path.
        /// </para>
        /// </remarks>
        public NntpSpoolWriteQueue(
            IOptions<NntpServerOptions> options,
            NntpSpoolMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(metrics);

            _capacity = options.Value.SpoolQueueCapacity;
            _maxQueuedBytes = options.Value.MaxQueuedBytes;
            if (_capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Spool queue capacity must be positive.");
            }

            if (_maxQueuedBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Spool max queued bytes must be positive.");
            }

            _metrics = metrics;
            Channel<NntpSpoolWriteItem> channel = Channel.CreateBounded<NntpSpoolWriteItem>(
                new BoundedChannelOptions(_capacity)
                {
                    // Defensive only — admission is enforced by _depth/_queuedBytes before TryWrite.
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = false,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                });
            _writer = channel.Writer;
            Reader = channel.Reader;
        }

        /// <summary>
        /// Gets the shared channel reader consumed exclusively by <see cref="NntpSpoolWriterPump"/> worker tasks.
        /// </summary>
        /// <remarks>
        /// Multiple pump workers may read concurrently. Callers must not complete this reader; shutdown completion is
        /// driven by <see cref="Complete"/> on the writer side. Items remain reflected in <see cref="Depth"/> and
        /// <see cref="QueuedBytes"/> until <see cref="NotifyDequeued(int)"/> runs after processing.
        /// </remarks>
        public ChannelReader<NntpSpoolWriteItem> Reader { get; }

        /// <summary>
        /// Gets the configured maximum queued item count frozen at construction.
        /// </summary>
        /// <remarks>
        /// Sourced from <see cref="NntpServerOptions.SpoolQueueCapacity"/>. Forwarded to
        /// <see cref="ISpoolWriterScalingPolicy.ComputeDesiredWriters(long, int)"/>; the default
        /// <see cref="ProcessorQueueSpoolWriterScalingPolicy"/> ignores capacity and scales from absolute depth only.
        /// </remarks>
        public int Capacity => _capacity;

        /// <summary>
        /// Gets the configured maximum total queued payload bytes frozen at construction.
        /// </summary>
        /// <remarks>
        /// Sourced from <see cref="NntpServerOptions.MaxQueuedBytes"/>. Enforced per article using
        /// <c>item.ArticleBytes.Length</c> at enqueue time.
        /// </remarks>
        public long MaxQueuedBytes => _maxQueuedBytes;

        /// <summary>
        /// Gets the current queued item count without acquiring <see cref="_sync"/>.
        /// </summary>
        /// <remarks>
        /// Includes channel-backed items and items dequeued by pump workers but not yet
        /// <see cref="NotifyDequeued"/>. May be briefly stale relative to concurrent enqueue or dequeue activity.
        /// </remarks>
        public long Depth => Volatile.Read(ref _depth);

        /// <summary>
        /// Gets the current queued payload byte total without acquiring <see cref="_sync"/>.
        /// </summary>
        /// <remarks>
        /// Sum of <c>ArticleBytes.Length</c> for items counted by <see cref="Depth"/>. May be briefly stale relative
        /// to concurrent activity.
        /// </remarks>
        public long QueuedBytes => Volatile.Read(ref _queuedBytes);

        /// <summary>
        /// Attempts to enqueue a spool write item while enforcing item-count and byte budgets.
        /// </summary>
        /// <param name="item">
        /// Write item to enqueue. Byte-budget admission uses <c>item.ArticleBytes.Length</c>; the array is not copied by
        /// this method.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the item is written and counters are updated; <see langword="false"/> when either
        /// budget is exceeded, the channel rejects the write (for example after <see cref="Complete"/>), or admission
        /// fails for any other reason.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Payload size is read before acquiring <see cref="_sync"/> so the lock is not held while inspecting
        /// <paramref name="item"/>. Byte-budget admission uses <c>queuedBytes &gt; _maxQueuedBytes - payloadBytes</c>
        /// to avoid <see cref="long"/> overflow on addition.
        /// </para>
        /// <para>
        /// Rejection records <see cref="NntpSpoolMetrics.RecordEnqueueRejected"/> with the pre-attempt depth and byte
        /// totals. Success records <see cref="NntpSpoolMetrics.RecordEnqueued"/> with updated totals. Safe for concurrent
        /// calls from multiple transit sessions.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is <see langword="null"/>.</exception>
        public bool TryEnqueue(NntpSpoolWriteItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            int payloadBytes = item.ArticleBytes.Length;
            lock (_sync)
            {
                long depth = _depth;
                long queuedBytes = _queuedBytes;
                if (depth >= _capacity || queuedBytes > _maxQueuedBytes - payloadBytes)
                {
                    _metrics.RecordEnqueueRejected(depth, queuedBytes);
                    return false;
                }

                if (!_writer.TryWrite(item))
                {
                    _metrics.RecordEnqueueRejected(depth, queuedBytes);
                    return false;
                }

                _depth = depth + 1;
                _queuedBytes = queuedBytes + payloadBytes;
                _metrics.RecordEnqueued(_depth, _queuedBytes);
                return true;
            }
        }

        /// <summary>
        /// Decrements queue counters after <see cref="NntpSpoolWriterPump"/> finishes processing a dequeued item.
        /// </summary>
        /// <param name="dequeuedBytes">
        /// Payload size in bytes for the processed item, typically <c>item.ArticleBytes.Length</c> from the dequeued
        /// <see cref="NntpSpoolWriteItem"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Must be invoked exactly once per successful channel read, after processing completes (including failure
        /// paths). <see cref="NntpSpoolWriterPump"/> calls this from a <c>finally</c> block so in-flight work remains
        /// visible in <see cref="Depth"/> and <see cref="QueuedBytes"/> until processing ends.
        /// </para>
        /// <para>
        /// Uses <see cref="Math.Max(long, long)"/> when decrementing so transient accounting mismatches cannot drive
        /// counters negative. Updates <see cref="NntpSpoolMetrics.RecordDequeued"/> under <see cref="_sync"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="dequeuedBytes"/> is negative.</exception>
        public void NotifyDequeued(int dequeuedBytes)
        {
            if (dequeuedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dequeuedBytes), "Dequeued byte count cannot be negative.");
            }

            lock (_sync)
            {
                _depth = Math.Max(0, _depth - 1);
                _queuedBytes = Math.Max(0, _queuedBytes - dequeuedBytes);
                _metrics.RecordDequeued(_depth, _queuedBytes);
            }
        }

        /// <summary>
        /// Signals that no further items will be admitted so pump workers can drain and exit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Delegates to <see cref="ChannelWriter{T}.TryComplete"/>, which is idempotent. Does not reset
        /// <see cref="Depth"/> or <see cref="QueuedBytes"/>; remaining items stay accounted until workers call
        /// <see cref="NotifyDequeued"/>.
        /// </para>
        /// <para>
        /// After completion, <see cref="TryEnqueue"/> may fail at <see cref="ChannelWriter{T}.TryWrite"/> even when
        /// counters show available capacity. Transit producers must treat <see langword="false"/> as backpressure or
        /// shutdown rejection.
        /// </para>
        /// </remarks>
        public void Complete()
        {
            _writer.TryComplete();
        }
    }
}
