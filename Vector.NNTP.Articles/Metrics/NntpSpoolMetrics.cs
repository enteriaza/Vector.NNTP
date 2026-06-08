// <copyright file="NntpSpoolMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: lock-free gauges and counters for transit spool queue and writer workers.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Vector.NNTP.Articles.Classification;
using Vector.NNTP.Articles.Logging;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.Articles.Metrics
{
    /// <summary>
    /// OpenTelemetry <see cref="Meter"/> instruments for transit spool queue admission, writer throughput, article
    /// outcomes, and worker pool observability.
    /// </summary>
    /// <remarks>
    /// <para><b>Producers:</b></para>
    /// <list type="bullet">
    /// <item><description><see cref="NntpSpoolWriteQueue"/> — <see cref="RecordEnqueued"/>, <see cref="RecordEnqueueRejected"/>, and <see cref="RecordDequeued"/>.</description></item>
    /// <item><description><see cref="NntpSpoolWriterPump"/> — preprocess/postprocess/write failures, successful writes, article types, accept/reject outcomes, and HistoryDB cleanup faults.</description></item>
    /// <item><description><see cref="NntpSpoolWriterPool"/> — <see cref="SetActiveWriters"/> for the active worker gauge.</description></item>
    /// <item><description><see cref="NntpSpoolTransitStorage"/> — enqueue-time <see cref="RecordArticleRejected"/> on max-size and queue-full paths.</description></item>
    /// <item><description><see cref="Hosting.NntpSpoolThroughputLogHostedService"/> — reader of <see cref="TakeMinuteSnapshotAndReset"/> (not a writer).</description></item>
    /// </list>
    /// <para>
    /// <b>Two rejection planes:</b> <c>nntp.spool.queue.rejected</c> counts admission failures inside
    /// <see cref="NntpSpoolWriteQueue.TryEnqueue"/> (depth/byte budget or closed channel). Tagged
    /// <c>nntp.spool.article.rejected</c> counts final article rejections with <c>feed</c> and <c>category</c> dimensions
    /// aligned with <see cref="INntpNewsLog.LogRejected"/>. A transit queue-full path may increment both when
    /// <see cref="NntpSpoolTransitStorage.TakeThisAsync"/> records an article rejection after
    /// <see cref="RecordEnqueueRejected"/> runs inside the queue.
    /// </para>
    /// <para>
    /// <b>Gauge model:</b> <see cref="_queueDepth"/> and <see cref="_queuedBytes"/> mirror
    /// <see cref="NntpSpoolWriteQueue"/> accounting (including in-flight items not yet
    /// <see cref="NntpSpoolWriteQueue.NotifyDequeued"/>). Observable gauges read them with
    /// <see cref="Volatile"/> reads; writers publish with <see cref="Interlocked.Exchange(ref long, long)"/>.
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
        /// All instances of <see cref="NntpSpoolMetrics"/> register instruments on this static meter exactly once per
        /// process.
        /// </remarks>
        private static readonly Meter Meter = new("Vector.NNTP.Articles", AssemblyInfoUtilities.ApplicationVersion);

        /// <summary>
        /// Counter instrument <c>nntp.spool.queue.enqueued</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordEnqueued"/> on each successful <see cref="NntpSpoolWriteQueue.TryEnqueue"/>.
        /// </remarks>
        private readonly Counter<long> _queueEnqueued;

        /// <summary>
        /// Counter instrument <c>nntp.spool.queue.rejected</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordEnqueueRejected"/> when <see cref="NntpSpoolWriteQueue.TryEnqueue"/>
        /// rejects admission. Does not carry feed or rejection-category tags.
        /// </remarks>
        private readonly Counter<long> _queueRejected;

        /// <summary>
        /// Counter instrument <c>nntp.spool.queue.dequeued</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordDequeued"/> once per completed pump item after
        /// <see cref="NntpSpoolWriteQueue.NotifyDequeued"/>.
        /// </remarks>
        private readonly Counter<long> _queueDequeued;

        /// <summary>
        /// Counter instrument <c>nntp.spool.write.success</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordWriteSuccess"/> once per successful
        /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/> on the writer path.
        /// </remarks>
        private readonly Counter<long> _writesSucceeded;

        /// <summary>
        /// Counter instrument <c>nntp.spool.write.failure</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordWriteFailure"/> when disk write or digest directory preparation fails after
        /// postprocessing succeeded.
        /// </remarks>
        private readonly Counter<long> _writesFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.preprocess.failure</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordPreprocessFailure"/> when
        /// <see cref="Processing.ArticleSpoolPreprocessor.PreprocessAsync"/> returns a failed result.
        /// </remarks>
        private readonly Counter<long> _preprocessFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.postprocess.failure</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordPostprocessFailure"/> when
        /// <see cref="Processing.ArticleSpoolPostprocessor.PostprocessAsync"/> returns a failed result (including spam
        /// classification rejections). Spamd fail-open faults do not increment this counter.
        /// </remarks>
        private readonly Counter<long> _postprocessFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.history.release_failure</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordHistoryReleaseFailure"/> when HistoryDB reservation release after a spool
        /// failure does not complete successfully.
        /// </remarks>
        private readonly Counter<long> _historyReleaseFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.history.commit_failure</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordHistoryCommitFailure"/> when HistoryDB reservation commit after a successful
        /// spool write does not complete successfully.
        /// </remarks>
        private readonly Counter<long> _historyCommitFailed;

        /// <summary>
        /// Counter instrument <c>nntp.spool.payload.bytes_written</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordWriteSuccess"/> with the written payload size (bytes throughput, not article
        /// count).
        /// </remarks>
        private readonly Counter<long> _payloadBytesWritten;

        /// <summary>
        /// Counter instrument <c>article_type_total</c> tagged by <c>type</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordArticleTypes"/> for each mapped <see cref="ArticleTypeFlags"/> bit on
        /// accepted articles.
        /// </remarks>
        private readonly Counter<long> _articleTypeTotal;

        /// <summary>
        /// Counter instrument <c>nntp.spool.article.accepted</c> tagged by <c>feed</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordArticleAccepted"/> on successful spool commits aligned with
        /// <see cref="INntpNewsLog.LogAccepted"/>.
        /// </remarks>
        private readonly Counter<long> _articleAccepted;

        /// <summary>
        /// Counter instrument <c>nntp.spool.article.rejected</c> tagged by <c>feed</c> and <c>category</c>.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="RecordArticleRejected"/> on final rejections aligned with
        /// <see cref="INntpNewsLog.LogRejected"/>.
        /// </remarks>
        private readonly Counter<long> _articleRejected;

        /// <summary>
        /// Per-feed accept/reject minute buckets drained by <see cref="TakeMinuteSnapshotAndReset"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keys are feed names from <see cref="NntpNewsFeedResolver"/> using <see cref="StringComparer.Ordinal"/>. Entries
        /// are created lazily by <see cref="GetFeedCounters"/> and are not removed after idle windows — the dictionary
        /// grows with the set of distinct feeds observed over process lifetime.
        /// </para>
        /// </remarks>
        private readonly ConcurrentDictionary<string, SpoolFeedOutcomeCounters> _feedOutcomeBuckets = new(StringComparer.Ordinal);

        /// <summary>
        /// Backing value for observable gauge <c>nntp.spool.queue.depth</c>.
        /// </summary>
        /// <remarks>
        /// Updated by <see cref="RecordEnqueued"/>, <see cref="RecordEnqueueRejected"/>, and
        /// <see cref="RecordDequeued"/> via <see cref="Interlocked.Exchange(ref long, long)"/>.
        /// </remarks>
        private long _queueDepth;

        /// <summary>
        /// Backing value for observable gauge <c>nntp.spool.queue.bytes</c>.
        /// </summary>
        /// <remarks>
        /// Sum of queued article payload bytes corresponding to <see cref="_queueDepth"/>, published by the same record
        /// methods.
        /// </remarks>
        private long _queuedBytes;

        /// <summary>
        /// Backing value for observable gauge <c>nntp.spool.writers.active</c>.
        /// </summary>
        /// <remarks>
        /// Published by <see cref="SetActiveWriters"/> when <see cref="NntpSpoolWriterPool"/> adjusts worker
        /// count during startup, scale-up, scale-down, or shutdown.
        /// </remarks>
        private long _activeWriters;

        /// <summary>
        /// Histogram instrument <c>nntp.spool.preprocess.duration_ms</c>.
        /// </summary>
        private readonly Histogram<double> _preprocessDurationMs;

        /// <summary>
        /// Histogram instrument <c>nntp.spool.postprocess.duration_ms</c>.
        /// </summary>
        private readonly Histogram<double> _postprocessDurationMs;

        /// <summary>
        /// Histogram instrument <c>nntp.spool.write.duration_ms</c>.
        /// </summary>
        private readonly Histogram<double> _writeDurationMs;

        /// <summary>
        /// Histogram instrument <c>nntp.spool.spamd.duration_ms</c>.
        /// </summary>
        private readonly Histogram<double> _spamdDurationMs;

        /// <summary>
        /// Counter instrument <c>nntp.spool.spamd.fail_open</c> tagged by <c>reason</c>.
        /// </summary>
        private readonly Counter<long> _spamdFailOpen;

        /// <summary>
        /// Counter instrument <c>nntp.spool.writers.scale_total</c> tagged by <c>direction</c>.
        /// </summary>
        private readonly Counter<long> _writerScaleTotal;

        /// <summary>
        /// Counter instrument <c>nntp.spool.queue.saturation_log</c> for rate-limited operator visibility of enqueue rejects.
        /// </summary>
        private readonly Counter<long> _queueSaturationLog;

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
        /// <item><description><c>nntp.spool.history.commit_failure</c></description></item>
        /// <item><description><c>nntp.spool.payload.bytes_written</c></description></item>
        /// <item><description><c>article_type_total</c> (tagged by <c>type</c>)</description></item>
        /// <item><description><c>nntp.spool.article.accepted</c> (tagged by <c>feed</c>)</description></item>
        /// <item><description><c>nntp.spool.article.rejected</c> (tagged by <c>feed</c> and <c>category</c>)</description></item>
        /// <item><description><c>nntp.spool.spamd.fail_open</c> (tagged by <c>reason</c>)</description></item>
        /// <item><description><c>nntp.spool.writers.scale_total</c> (tagged by <c>direction</c>)</description></item>
        /// <item><description><c>nntp.spool.queue.saturation_log</c></description></item>
        /// </list>
        /// <para><b>Histograms:</b> <c>nntp.spool.preprocess.duration_ms</c>, <c>nntp.spool.postprocess.duration_ms</c>,
        /// <c>nntp.spool.write.duration_ms</c>, <c>nntp.spool.spamd.duration_ms</c>.</para>
        /// <para><b>Observable gauges:</b> <c>nntp.spool.queue.depth</c>, <c>nntp.spool.queue.bytes</c>,
        /// <c>nntp.spool.writers.active</c> — sampled via callbacks that read backing fields with
        /// <see cref="Volatile"/> reads.</para>
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
            _historyCommitFailed = Meter.CreateCounter<long>("nntp.spool.history.commit_failure");
            _payloadBytesWritten = Meter.CreateCounter<long>("nntp.spool.payload.bytes_written");
            _articleTypeTotal = Meter.CreateCounter<long>("article_type_total");
            _articleAccepted = Meter.CreateCounter<long>("nntp.spool.article.accepted");
            _articleRejected = Meter.CreateCounter<long>("nntp.spool.article.rejected");
            _preprocessDurationMs = Meter.CreateHistogram<double>("nntp.spool.preprocess.duration_ms", unit: "ms");
            _postprocessDurationMs = Meter.CreateHistogram<double>("nntp.spool.postprocess.duration_ms", unit: "ms");
            _writeDurationMs = Meter.CreateHistogram<double>("nntp.spool.write.duration_ms", unit: "ms");
            _spamdDurationMs = Meter.CreateHistogram<double>("nntp.spool.spamd.duration_ms", unit: "ms");
            _spamdFailOpen = Meter.CreateCounter<long>("nntp.spool.spamd.fail_open");
            _writerScaleTotal = Meter.CreateCounter<long>("nntp.spool.writers.scale_total");
            _queueSaturationLog = Meter.CreateCounter<long>("nntp.spool.queue.saturation_log");

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
        /// New queued item count after enqueue, typically <see cref="NntpSpoolWriteQueue.Depth"/> under the queue
        /// lock.
        /// </param>
        /// <param name="queuedBytes">
        /// New queued payload byte total after enqueue, typically <see cref="NntpSpoolWriteQueue.QueuedBytes"/>.
        /// </param>
        /// <remarks>
        /// Increments <c>nntp.spool.queue.enqueued</c> by one and publishes <paramref name="depth"/> and
        /// <paramref name="queuedBytes"/> to observable gauges. Called only from
        /// <see cref="NntpSpoolWriteQueue.TryEnqueue"/> after a successful channel write. Never throws.
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
        /// <para>
        /// Increments <c>nntp.spool.queue.rejected</c> by one. Gauge values are still published so observers sampling
        /// after a reject see consistent depth/byte totals.
        /// </para>
        /// <para>
        /// Does not increment <c>nntp.spool.article.rejected</c> by itself — article-level rejection metrics are recorded
        /// separately by <see cref="NntpSpoolTransitStorage"/> when appropriate. Never throws.
        /// </para>
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
        /// New queued item count after <see cref="NntpSpoolWriteQueue.NotifyDequeued"/>, typically reduced by
        /// one from the pre-dequeue value.
        /// </param>
        /// <param name="queuedBytes">
        /// New queued byte total after dequeue accounting.
        /// </param>
        /// <remarks>
        /// Increments <c>nntp.spool.queue.dequeued</c> by one. Invoked from
        /// <see cref="NntpSpoolWriterPump"/> <c>finally</c> blocks after preprocessing, postprocessing, and write
        /// attempts complete so in-flight work remains visible in queue gauges until processing ends. Never throws.
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
        /// Written article byte count (typically postprocessed payload length). Values <c>&lt;= 0</c> increment only the
        /// success counter, not <c>nntp.spool.payload.bytes_written</c>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Increments <c>nntp.spool.write.success</c> by one (article count) and, when <paramref name="payloadBytes"/>
        /// is positive, adds the same value to <c>nntp.spool.payload.bytes_written</c> (throughput). Operators can derive
        /// articles/sec from the success counter and bytes/sec from the payload counter.
        /// </para>
        /// <para>
        /// Called after <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/> succeeds. Pair with
        /// <see cref="RecordArticleAccepted"/> for outcome metrics. Never throws.
        /// </para>
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
        /// <para>
        /// Increments <c>nntp.spool.write.failure</c> by one. Typically followed by operator error logs, an article
        /// rejection outcome via <see cref="RecordArticleRejected"/>, and a HistoryDB release attempt from
        /// <see cref="NntpSpoolWriterPump"/>.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        internal void RecordWriteFailure()
        {
            _writesFailed.Add(1);
        }

        /// <summary>
        /// Records an <see cref="Processing.ArticleSpoolPreprocessor"/> failure for a dequeued item.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Increments <c>nntp.spool.preprocess.failure</c> by one. Does not affect queue depth gauges — dequeue
        /// accounting still runs in the pump <c>finally</c> block.
        /// </para>
        /// <para>
        /// Pair with <see cref="RecordArticleRejected"/> and news-log rejection on the pump path. Never throws.
        /// </para>
        /// </remarks>
        internal void RecordPreprocessFailure()
        {
            _preprocessFailed.Add(1);
        }

        /// <summary>
        /// Records an <see cref="Processing.ArticleSpoolPostprocessor"/> failure for a dequeued item.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Increments <c>nntp.spool.postprocess.failure</c> by one. Does not affect queue depth gauges — dequeue
        /// accounting still runs in the pump <c>finally</c> block.
        /// </para>
        /// <para>
        /// Includes spam classification rejections. Spamd protocol/connectivity fail-open paths do not call this method.
        /// Pair with <see cref="RecordArticleRejected"/> on rejection. Never throws.
        /// </para>
        /// </remarks>
        internal void RecordPostprocessFailure()
        {
            _postprocessFailed.Add(1);
        }

        /// <summary>
        /// Records a HistoryDB reservation release failure after spool preprocess, postprocess, or write failure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Increments <c>nntp.spool.history.release_failure</c> when
        /// <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/> returns a non-success outcome or throws
        /// during pump cleanup.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        internal void RecordHistoryReleaseFailure()
        {
            _historyReleaseFailed.Add(1);
        }

        /// <summary>
        /// Records a HistoryDB reservation commit failure after successful spool persistence.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Increments <c>nntp.spool.history.commit_failure</c> when
        /// <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryRecordAsync"/> returns a non-success outcome or throws
        /// after <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/> succeeded.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        internal void RecordHistoryCommitFailure()
        {
            _historyCommitFailed.Add(1);
        }

        /// <summary>
        /// Publishes the current active spool writer worker count to the observable gauge.
        /// </summary>
        /// <param name="writers">
        /// Active worker count, typically from <see cref="NntpSpoolWriterPool"/> after pool startup, scaling, or
        /// shutdown adjustments.
        /// </param>
        /// <remarks>
        /// Updates <see cref="_activeWriters"/> via <see cref="Interlocked.Exchange(ref long, long)"/>. Negative values
        /// are not expected from callers. Never throws.
        /// </remarks>
        internal void SetActiveWriters(int writers)
        {
            _ = Interlocked.Exchange(ref _activeWriters, writers);
        }

        /// <summary>
        /// Records preprocess wall time for a dequeued spool item.
        /// </summary>
        /// <param name="durationMs">Elapsed milliseconds from preprocess start to completion.</param>
        /// <remarks>
        /// Observed by <see cref="Storage.NntpSpoolWriterPump"/> after
        /// <see cref="Processing.ArticleSpoolPreprocessor.PreprocessAsync"/> returns. Never throws.
        /// </remarks>
        internal void RecordPreprocessDuration(double durationMs)
        {
            if (durationMs >= 0)
            {
                _preprocessDurationMs.Record(durationMs);
            }
        }

        /// <summary>
        /// Records postprocess wall time for a dequeued spool item.
        /// </summary>
        /// <param name="durationMs">Elapsed milliseconds from postprocess start to completion.</param>
        /// <remarks>
        /// Observed by <see cref="Storage.NntpSpoolWriterPump"/> after
        /// <see cref="Processing.ArticleSpoolPostprocessor.PostprocessAsync"/> returns. Never throws.
        /// </remarks>
        internal void RecordPostprocessDuration(double durationMs)
        {
            if (durationMs >= 0)
            {
                _postprocessDurationMs.Record(durationMs);
            }
        }

        /// <summary>
        /// Records atomic spool write wall time for a dequeued item.
        /// </summary>
        /// <param name="durationMs">Elapsed milliseconds for digest directory preparation and atomic write.</param>
        /// <remarks>
        /// Observed by <see cref="Storage.NntpSpoolWriterPump"/> around disk I/O. Never throws.
        /// </remarks>
        internal void RecordWriteDuration(double durationMs)
        {
            if (durationMs >= 0)
            {
                _writeDurationMs.Record(durationMs);
            }
        }

        /// <summary>
        /// Records SpamAssassin round-trip wall time for a postprocess check.
        /// </summary>
        /// <param name="durationMs">Elapsed milliseconds for the spamd protocol exchange.</param>
        /// <remarks>
        /// Observed by <see cref="Processing.ArticleSpoolPostprocessor"/> on successful spamd responses and fail-open
        /// faults (duration still reflects time spent before the fault). Never throws.
        /// </remarks>
        internal void RecordSpamdDuration(double durationMs)
        {
            if (durationMs >= 0)
            {
                _spamdDurationMs.Record(durationMs);
            }
        }

        /// <summary>
        /// Records a SpamAssassin fail-open event when postprocess accepts an article despite spamd faults.
        /// </summary>
        /// <param name="reason">
        /// Coarse fault bucket (for example <c>timeout</c>, <c>connect</c>, <c>protocol</c>) for dashboard grouping.
        /// </param>
        /// <remarks>
        /// Increments <c>nntp.spool.spamd.fail_open</c> with a <c>reason</c> tag. Pair with warning logs from
        /// <see cref="Processing.ArticleSpoolPostprocessor"/>. Never throws.
        /// </remarks>
        internal void RecordSpamdFailOpen(string reason)
        {
            _spamdFailOpen.Add(1, new KeyValuePair<string, object?>("reason", reason));
        }

        /// <summary>
        /// Records a writer pool scale-up or scale-down adjustment.
        /// </summary>
        /// <param name="direction">Literal <c>up</c> or <c>down</c> matching pool scaling direction.</param>
        /// <remarks>
        /// Incremented by <see cref="Storage.NntpSpoolWriterPool"/> when worker count changes. Never throws.
        /// </remarks>
        internal void RecordWriterScale(string direction)
        {
            _writerScaleTotal.Add(1, new KeyValuePair<string, object?>("direction", direction));
        }

        /// <summary>
        /// Records a rate-limited operator visibility event when enqueue reject pressure is elevated.
        /// </summary>
        /// <remarks>
        /// Incremented by <see cref="Storage.NntpSpoolTransitStorage"/> when sustained queue-full or max-size rejections
        /// trigger a warning log. Never throws.
        /// </remarks>
        internal void RecordQueueSaturationLog()
        {
            _queueSaturationLog.Add(1);
        }

        /// <summary>
        /// Records <see cref="ArticleTypeFlags"/> for a successfully postprocessed article that will be or was written.
        /// </summary>
        /// <param name="articleType">
        /// Classification flags from <see cref="Processing.ArticleSpoolPostprocessor"/> on the success path before disk
        /// write.
        /// </param>
        /// <remarks>
        /// <para>
        /// Increments <c>article_type_total</c> once per mapped flag bit set on <paramref name="articleType"/>. When no
        /// mapped flag is present, increments <c>type="default"</c> once so plain-text volume is visible on dashboards.
        /// </para>
        /// <para>
        /// Tag names are defined by <see cref="ArticleTypeMetricsTags"/> (for example <c>yenc</c>, <c>archive</c>,
        /// <c>video</c>, <c>text</c>). Multiple flags on one article emit multiple increments. Never throws.
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

        /// <summary>
        /// Records a spool commit accept outcome aligned with <see cref="INntpNewsLog.LogAccepted"/>.
        /// </summary>
        /// <param name="origin">Enqueue origin metadata for feed resolution.</param>
        /// <param name="articleBytes">Committed article bytes used for <c>Path</c> feed fallback in <see cref="NntpNewsFeedResolver"/>.</param>
        /// <remarks>
        /// <para>
        /// Increments <c>nntp.spool.article.accepted</c> with a <c>feed</c> tag and the per-feed minute accept bucket via
        /// <see cref="SpoolFeedOutcomeCounters.RecordAccepted"/>. Feed names match <see cref="NntpNewsFeedResolver"/>.
        /// </para>
        /// <para>
        /// Called from <see cref="NntpSpoolWriterPump"/> only after a successful durable write, not merely after
        /// postprocess success. Never throws.
        /// </para>
        /// </remarks>
        internal void RecordArticleAccepted(in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            string feed = NntpNewsFeedResolver.ResolveFeed(origin, articleBytes);
            _articleAccepted.Add(1, new KeyValuePair<string, object?>("feed", feed));
            GetFeedCounters(feed).RecordAccepted();
        }

        /// <summary>
        /// Records a final spool rejection outcome aligned with <see cref="INntpNewsLog.LogRejected"/>.
        /// </summary>
        /// <param name="origin">Enqueue origin metadata for feed resolution.</param>
        /// <param name="articleBytes">Article bytes available at rejection time for <c>Path</c> feed fallback.</param>
        /// <param name="category">Coarse rejection bucket from <see cref="SpoolArticleRejectionClassifier"/>.</param>
        /// <remarks>
        /// <para>
        /// Increments <c>nntp.spool.article.rejected</c> with <c>feed</c> and <c>category</c> tags (category string from
        /// <see cref="SpoolArticleRejectionMetricsTags.GetTag"/>) and the matching per-feed minute rejection bucket via
        /// <see cref="SpoolFeedOutcomeCounters.RecordRejected"/>.
        /// </para>
        /// <para>
        /// Called from <see cref="NntpSpoolWriterPump"/> on preprocess/postprocess/write failures and from
        /// <see cref="NntpSpoolTransitStorage"/> on enqueue-time size and queue-full rejections. Never throws.
        /// </para>
        /// </remarks>
        internal void RecordArticleRejected(
            in NntpSpoolArticleOrigin origin,
            ReadOnlySpan<byte> articleBytes,
            SpoolArticleRejectionCategory category)
        {
            string feed = NntpNewsFeedResolver.ResolveFeed(origin, articleBytes);
            string categoryTag = SpoolArticleRejectionMetricsTags.GetTag(category);
            _articleRejected.Add(
                1,
                new KeyValuePair<string, object?>("feed", feed),
                new KeyValuePair<string, object?>("category", categoryTag));
            GetFeedCounters(feed).RecordRejected(category);
        }

        /// <summary>
        /// Captures per-feed accept/reject deltas since the previous call and resets minute buckets to zero.
        /// </summary>
        /// <returns>
        /// A <see cref="SpoolThroughputMinuteSnapshot"/> with a global rollup row and alphabetically sorted per-feed rows
        /// omitting feeds with zero <see cref="SpoolThroughputFeedCounts.Processed"/> in the window. Idle feed buckets
        /// remain in <see cref="_feedOutcomeBuckets"/> for future activity.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Called once per minute by <see cref="Hosting.NntpSpoolThroughputLogHostedService"/>. Drains every entry in
        /// <see cref="_feedOutcomeBuckets"/> via <see cref="SpoolFeedOutcomeCounters.TakeSnapshotAndReset"/>, sums active
        /// rows into the global rollup, and sorts feed rows ordinally. Concurrent
        /// <see cref="RecordArticleAccepted"/> / <see cref="RecordArticleRejected"/> calls during the drain accrue in the
        /// next window.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        internal SpoolThroughputMinuteSnapshot TakeMinuteSnapshotAndReset()
        {
            List<SpoolThroughputFeedCounts> feedRows = [];
            long accepted = 0;
            long headerSyntax = 0;
            long crc = 0;
            long crosspost = 0;
            long other = 0;

            foreach (KeyValuePair<string, SpoolFeedOutcomeCounters> entry in _feedOutcomeBuckets)
            {
                SpoolThroughputFeedCounts row = entry.Value.TakeSnapshotAndReset(entry.Key);
                if (row.Processed <= 0)
                {
                    continue;
                }

                feedRows.Add(row);
                accepted += row.Accepted;
                headerSyntax += row.HeaderSyntax;
                crc += row.Crc;
                crosspost += row.Crosspost;
                other += row.Other;
            }

            feedRows.Sort(static (left, right) => string.Compare(left.Feed, right.Feed, StringComparison.Ordinal));

            SpoolThroughputFeedCounts global = new(
                SpoolThroughputMinuteSnapshot.GlobalFeedLabel,
                accepted,
                headerSyntax,
                crc,
                crosspost,
                other);

            return new SpoolThroughputMinuteSnapshot(global, feedRows);
        }

        /// <summary>
        /// Gets or creates the lock-free counter bucket for a feed name.
        /// </summary>
        /// <param name="feed">Resolved feed token from <see cref="NntpNewsFeedResolver"/>.</param>
        /// <returns>
        /// Mutable <see cref="SpoolFeedOutcomeCounters"/> bucket for <paramref name="feed"/>, created on first outcome
        /// recorded for that feed name.
        /// </returns>
        /// <remarks>
        /// Uses lazy <c>GetOrAdd</c> on <see cref="_feedOutcomeBuckets"/> with ordinal feed keys. Never throws.
        /// </remarks>
        private SpoolFeedOutcomeCounters GetFeedCounters(string feed)
        {
            return _feedOutcomeBuckets.GetOrAdd(feed, static _ => new SpoolFeedOutcomeCounters());
        }
    }
}
