// <copyright file="NntpSpoolMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: lock-free gauges and counters for transit spool queue and writer workers.

using System.Diagnostics.Metrics;
using Vector.NNTP.Articles.Classification;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// OpenTelemetry <see cref="Meter"/> instruments for transit spool queue admission, writer throughput, and worker
    /// pool observability.
    /// </summary>
    /// <remarks>
    /// <para><b>Producers:</b></para>
    /// <list type="bullet">
    /// <item><description><see cref="Storage.NntpSpoolWriteQueue"/> — enqueue, reject, and dequeue counters/gauges.</description></item>
    /// <item><description><see cref="Storage.NntpSpoolWriterPump"/> — preprocess/postprocess failure, write success/failure, payload bytes, HistoryDB release failures.</description></item>
    /// <item><description><see cref="Storage.NntpSpoolWriterPool"/> — active writer gauge via <see cref="SetActiveWriters"/>.</description></item>
    /// </list>
    /// <para>
    /// <b>Gauge model:</b> <see cref="_queueDepth"/> and <see cref="_queuedBytes"/> mirror
    /// <see cref="Storage.NntpSpoolWriteQueue"/> accounting (including in-flight items not yet
    /// <see cref="Storage.NntpSpoolWriteQueue.NotifyDequeued"/>). Observable gauges read them with
    /// <see cref="M:System.Threading.Volatile.Read(System.Int64@)"/>; writers publish with
    /// <see cref="M:System.Threading.Interlocked.Exchange(System.Int64@,System.Int64)"/>.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Singleton via
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// </para>
    /// <para><b>Threading:</b> All recording methods are safe under concurrent queue producers and multiple pump workers.</para>
    /// </remarks>
    internal sealed class NntpSpoolMetrics
    {
        /// <summary>
        /// Shared <see cref="Meter"/> instance for the <c>Vector.NNTP.Articles</c> assembly.
        /// </summary>
        /// <remarks>
        /// Named <c>Vector.NNTP.Articles</c> with version <see cref="AssemblyInfoUtilities.ApplicationVersion"/>.
        /// All instances of <see cref="NntpSpoolMetrics"/> register instruments on this meter.
        /// </remarks>
        private static readonly Meter Meter = new("Vector.NNTP.Articles", AssemblyInfoUtilities.ApplicationVersion);

        /// <summary>
        /// Counter instrument <c>nntp.spool.queue.enqueued</c> incremented on successful
        /// <see cref="Storage.NntpSpoolWriteQueue.TryEnqueue"/>.
        /// </summary>
        private readonly Counter<long> _queueEnqueued;

        /// <summary>
        /// Counter instrument <c>nntp.spool.queue.rejected</c> incremented when
        /// <see cref="Storage.NntpSpoolWriteQueue.TryEnqueue"/> rejects admission.
        /// </summary>
        private readonly Counter<long> _queueRejected;

        /// <summary>
        /// Counter instrument <c>nntp.spool.queue.dequeued</c> incremented on each
        /// <see cref="Storage.NntpSpoolWriteQueue.NotifyDequeued"/>.
        /// </summary>
        private readonly Counter<long> _queueDequeued;

        /// <summary>
        /// Counter instrument <c>nntp.spool.write.success</c> incremented on successful atomic payload writes.
        /// </summary>
        private readonly Counter<long> _writesSucceeded;

        /// <summary>
        /// Counter instrument <c>nntp.spool.write.failure</c> incremented when disk write fails after preprocessing.
        /// </summary>
        private readonly Counter<long> _writesFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.preprocess.failure</c> incremented when
        /// <see cref="Processing.ArticleSpoolPreprocessor"/> returns a failed result.
        /// </summary>
        private readonly Counter<long> _preprocessFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.postprocess.failure</c> incremented when
        /// <see cref="Processing.ArticleSpoolPostprocessor"/> returns a failed result.
        /// </summary>
        private readonly Counter<long> _postprocessFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.history.release_failure</c> incremented when HistoryDB release after spool
        /// failure does not complete successfully.
        /// </summary>
        private readonly Counter<long> _historyReleaseFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.payload.bytes_written</c> tracking cumulative successful payload bytes.
        /// </summary>
        private readonly Counter<long> _payloadBytesWritten;

        /// <summary>
        /// Counter instrument <c>article_type_total</c> tagged by classified article content and control types.
        /// </summary>
        private readonly Counter<long> _articleTypeTotal;

        /// <summary>
        /// Backing value for observable gauge <c>nntp.spool.queue.depth</c>.
        /// </summary>
        /// <remarks>
        /// Updated by <see cref="RecordEnqueued"/>, <see cref="RecordEnqueueRejected"/>, and
        /// <see cref="RecordDequeued"/> via
        /// <see cref="M:System.Threading.Interlocked.Exchange(System.Int64@,System.Int64)"/>.
        /// </remarks>
        private long _queueDepth;

        /// <summary>
        /// Backing value for observable gauge <c>nntp.spool.queue.bytes</c>.
        /// </summary>
        /// <remarks>
        /// Sum of queued article payload bytes corresponding to <see cref="_queueDepth"/>.
        /// </remarks>
        private long _queuedBytes;

        /// <summary>
        /// Backing value for observable gauge <c>nntp.spool.writers.active</c>.
        /// </summary>
        /// <remarks>
        /// Published by <see cref="SetActiveWriters"/> when <see cref="Storage.NntpSpoolWriterPool"/> adjusts worker count.
        /// </remarks>
        private long _activeWriters;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolMetrics"/> class and registers spool instruments.
        /// </summary>
        /// <remarks>
        /// <para><b>Counters:</b></para>
        /// <list type="bullet">
        /// <item><description><c>nntp.spool.queue.enqueued</c></description></item>
        /// <item><description><c>nntp.spool.queue.rejected</c></description></item>
        /// <item><description><c>nntp.spool.queue.dequeued</c></description></item>
        /// <item><description><c>nntp.spool.write.success</c></description></item>
        /// <item><description><c>nntp.spool.write.failure</c></description></item>
        /// <item><description><c>nntp.spool.preprocess.failure</c></description></item>
        /// <item><description><c>nntp.spool.postprocess.failure</c></description></item>
        /// <item><description><c>nntp.spool.history.release_failure</c></description></item>
        /// <item><description><c>nntp.spool.payload.bytes_written</c></description></item>
        /// <item><description><c>article_type_total</c> (tagged by <c>type</c>)</description></item>
        /// </list>
        /// <para><b>Observable gauges:</b> <c>nntp.spool.queue.depth</c>, <c>nntp.spool.queue.bytes</c>,
        /// <c>nntp.spool.writers.active</c>.</para>
        /// </remarks>
        public NntpSpoolMetrics()
        {
            _queueEnqueued = Meter.CreateCounter<long>("nntp.spool.queue.enqueued");
            _queueRejected = Meter.CreateCounter<long>("nntp.spool.queue.rejected");
            _queueDequeued = Meter.CreateCounter<long>("nntp.spool.queue.dequeued");
            _writesSucceeded = Meter.CreateCounter<long>("nntp.spool.write.success");
            _writesFailed = Meter.CreateCounter<long>("nntp.spool.write.failure");
            _preprocessFailed = Meter.CreateCounter<long>("nntp.spool.preprocess.failure");
            _postprocessFailed = Meter.CreateCounter<long>("nntp.spool.postprocess.failure");
            _historyReleaseFailed = Meter.CreateCounter<long>("nntp.spool.history.release_failure");
            _payloadBytesWritten = Meter.CreateCounter<long>("nntp.spool.payload.bytes_written");
            _articleTypeTotal = Meter.CreateCounter<long>("article_type_total");

            _ = Meter.CreateObservableGauge(
                "nntp.spool.queue.depth",
                () => new Measurement<long>(Volatile.Read(ref _queueDepth)),
                description: "Pending spool write queue item count.");
            _ = Meter.CreateObservableGauge(
                "nntp.spool.queue.bytes",
                () => new Measurement<long>(Volatile.Read(ref _queuedBytes)),
                description: "Pending spool queue payload bytes.");
            _ = Meter.CreateObservableGauge(
                "nntp.spool.writers.active",
                () => new Measurement<long>(Volatile.Read(ref _activeWriters)),
                description: "Active spool writer worker count.");
        }

        /// <summary>
        /// Records a successful spool queue enqueue and refreshes queue depth gauges.
        /// </summary>
        /// <param name="depth">
        /// New queued item count after enqueue, typically <see cref="Storage.NntpSpoolWriteQueue.Depth"/>.
        /// </param>
        /// <param name="queuedBytes">
        /// New queued payload byte total after enqueue, typically <see cref="Storage.NntpSpoolWriteQueue.QueuedBytes"/>.
        /// </param>
        /// <remarks>
        /// Increments <c>nntp.spool.queue.enqueued</c> by one and publishes <paramref name="depth"/> and
        /// <paramref name="queuedBytes"/> to observable gauges.
        /// </remarks>
        internal void RecordEnqueued(long depth, long queuedBytes)
        {
            _queueEnqueued.Add(1);
            _ = Interlocked.Exchange(ref _queueDepth, depth);
            _ = Interlocked.Exchange(ref _queuedBytes, queuedBytes);
        }

        /// <summary>
        /// Records a rejected spool queue enqueue attempt and refreshes queue depth gauges.
        /// </summary>
        /// <param name="depth">
        /// Queue depth at rejection time (unchanged from the pre-attempt value when rejection occurs before enqueue).
        /// </param>
        /// <param name="queuedBytes">
        /// Queued byte total at rejection time.
        /// </param>
        /// <remarks>
        /// Increments <c>nntp.spool.queue.rejected</c> by one. Gauge values are still published so observers sampling
        /// after a reject see consistent depth/byte totals.
        /// </remarks>
        internal void RecordEnqueueRejected(long depth, long queuedBytes)
        {
            _queueRejected.Add(1);
            _ = Interlocked.Exchange(ref _queueDepth, depth);
            _ = Interlocked.Exchange(ref _queuedBytes, queuedBytes);
        }

        /// <summary>
        /// Records completion of spool queue accounting for a processed item and refreshes queue depth gauges.
        /// </summary>
        /// <param name="depth">
        /// New queued item count after <see cref="Storage.NntpSpoolWriteQueue.NotifyDequeued"/>, typically reduced by one.
        /// </param>
        /// <param name="queuedBytes">
        /// New queued byte total after dequeue accounting.
        /// </param>
        /// <remarks>
        /// Increments <c>nntp.spool.queue.dequeued</c> by one. Invoked from pump <c>finally</c> blocks after
        /// preprocessing and write attempts complete.
        /// </remarks>
        internal void RecordDequeued(long depth, long queuedBytes)
        {
            _queueDequeued.Add(1);
            _ = Interlocked.Exchange(ref _queueDepth, depth);
            _ = Interlocked.Exchange(ref _queuedBytes, queuedBytes);
        }

        /// <summary>
        /// Records a successful atomic spool payload write.
        /// </summary>
        /// <param name="payloadBytes">
        /// Written article byte count (typically preprocessed payload length). Values <c>&lt;= 0</c> increment only the
        /// success counter, not <c>nntp.spool.payload.bytes_written</c>.
        /// </param>
        /// <remarks>
        /// Increments <c>nntp.spool.write.success</c> by one (article count) and, when <paramref name="payloadBytes"/>
        /// is positive, adds the same value to <c>nntp.spool.payload.bytes_written</c> (throughput). Operators can derive
        /// articles/sec from the success counter and bytes/sec from the payload counter.
        /// </remarks>
        internal void RecordWriteSuccess(int payloadBytes)
        {
            _writesSucceeded.Add(1);
            if (payloadBytes > 0)
            {
                _payloadBytesWritten.Add(payloadBytes);
            }
        }

        /// <summary>
        /// Records a failed spool payload write after postprocessing succeeded.
        /// </summary>
        /// <remarks>
        /// Increments <c>nntp.spool.write.failure</c> by one. Typically followed by operator error logs and a HistoryDB
        /// release attempt from <see cref="Storage.NntpSpoolWriterPump"/>.
        /// </remarks>
        internal void RecordWriteFailure()
        {
            _writesFailed.Add(1);
        }

        /// <summary>
        /// Records an <see cref="Processing.ArticleSpoolPreprocessor"/> failure for a dequeued item.
        /// </summary>
        /// <remarks>
        /// Increments <c>nntp.spool.preprocess.failure</c> by one. Does not affect queue depth gauges — dequeue
        /// accounting still runs in the pump <c>finally</c> block.
        /// </remarks>
        internal void RecordPreprocessFailure()
        {
            _preprocessFailed.Add(1);
        }

        /// <summary>
        /// Records an <see cref="Processing.ArticleSpoolPostprocessor"/> failure for a dequeued item.
        /// </summary>
        /// <remarks>
        /// Increments <c>nntp.spool.postprocess.failure</c> by one. Does not affect queue depth gauges — dequeue
        /// accounting still runs in the pump <c>finally</c> block.
        /// </remarks>
        internal void RecordPostprocessFailure()
        {
            _postprocessFailed.Add(1);
        }

        /// <summary>
        /// Records a HistoryDB reservation release failure after spool preprocess or write failure.
        /// </summary>
        /// <remarks>
        /// Increments <c>nntp.spool.history.release_failure</c> when <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/>
        /// returns a non-success outcome or throws.
        /// </remarks>
        internal void RecordHistoryReleaseFailure()
        {
            _historyReleaseFailed.Add(1);
        }

        /// <summary>
        /// Publishes the current active spool writer worker count to the observable gauge.
        /// </summary>
        /// <param name="writers">
        /// Active worker count, typically from <see cref="Storage.NntpSpoolWriterPool.ActiveWriterCount"/> after pool
        /// startup, scaling, or shutdown adjustments.
        /// </param>
        /// <remarks>
        /// Updates <see cref="_activeWriters"/> via
        /// <see cref="M:System.Threading.Interlocked.Exchange(System.Int64@,System.Int64)"/>. Negative values are not expected
        /// from callers.
        /// </remarks>
        internal void SetActiveWriters(int writers)
        {
            _ = Interlocked.Exchange(ref _activeWriters, writers);
        }

        /// <summary>
        /// Records <see cref="Classification.ArticleTypeFlags"/> for a successfully postprocessed article.
        /// </summary>
        /// <param name="articleType">Classification flags from <see cref="Processing.ArticleSpoolPostprocessor"/>.</param>
        /// <remarks>
        /// <para>
        /// Increments <c>article_type_total</c> once per mapped flag bit set on <paramref name="articleType"/>. When no
        /// mapped flag is present, increments <c>type="default"</c> once so plain-text volume is visible on dashboards.
        /// </para>
        /// <para>
        /// Tag names are defined by <see cref="ArticleTypeMetricsTags"/> and include values such as
        /// <c>yenc</c>, <c>archive</c>, <c>video</c>, and <c>text</c>.
        /// </para>
        /// </remarks>
        internal void RecordArticleTypes(ArticleTypeFlags articleType)
        {
            bool emitted = false;
            foreach ((ArticleTypeFlags flag, string tag) in ArticleTypeMetricsTags.GetMappedTags())
            {
                if ((articleType & flag) == 0)
                {
                    continue;
                }

                _articleTypeTotal.Add(1, new KeyValuePair<string, object?>("type", tag));
                emitted = true;
            }

            if (!emitted)
            {
                _articleTypeTotal.Add(1, new KeyValuePair<string, object?>("type", ArticleTypeMetricsTags.DefaultTag));
            }
        }
    }
}
