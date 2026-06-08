// <copyright file="NntpSpoolTransitStorage.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: transit storage adapter that enqueues accepted TAKETHIS/IHAVE payloads.

using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Logging;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.HistoryDB.Encoding;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.Storage;

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Production <see cref="INntpTransitStorage"/> implementation that copies accepted transit article bytes into
    /// <see cref="NntpSpoolWriteQueue"/> for asynchronous spool disk write by <see cref="NntpSpoolWriterPump"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> NNTPD TAKETHIS and IHAVE command handlers invoke <see cref="TakeThisAsync"/> after
    /// <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryRecordAsync"/> reserves the message-id and the dot-stuffed
    /// body is decoded. This adapter is the boundary between protocol I/O and the bounded in-memory spool pipeline.
    /// </para>
    /// <para>
    /// <see cref="CheckAsync"/> and <see cref="IHaveAsync"/> are legacy <see cref="INntpTransitStorage"/> stubs that
    /// always return <see langword="true"/>. Production CHECK uses
    /// <see cref="HistoryDB.Abstractions.IHistoryDatabase.CheckAsync"/> in
    /// <see cref="Sockets.Transport.Commands.NntpCmdCheck"/>; production IHAVE offer policy uses HistoryDB in
    /// <see cref="Sockets.Transport.Commands.NntpCmdIHave"/> before body transfer. Neither handler calls the storage
    /// stubs today.
    /// </para>
    /// <para>
    /// <b>Admission ordering in <see cref="TakeThisAsync"/>:</b></para>
    /// <list type="number">
    /// <item><description>Reject oversize articles when <see cref="NntpServerOptions.MaxArtSize"/> is positive — no digest, array copy, or enqueue.</description></item>
    /// <item><description>Compute Blake3 digest hex and copy bytes into a <see cref="NntpSpoolWriteItem"/>.</description></item>
    /// <item><description>Attempt <see cref="NntpSpoolWriteQueue.TryEnqueue"/>; on failure the copied item is discarded but digest/copy cost is already paid.</description></item>
    /// </list>
    /// <para>
    /// A pre-enqueue queue-budget probe is intentionally omitted: BLAKE3 over a message-id is negligible relative to
    /// article handling, and handlers need a definitive <see cref="NntpTransitStorageResult"/> after the body is read.
    /// </para>
    /// <para>
    /// <b>Defense in depth:</b> Command handlers enforce <see cref="NntpServerOptions.MaxArtSize"/> while reading
    /// dot-stuffed bodies on the socket path (the NNTPD body reader enforces the same limit while decoding).
    /// Storage repeats the limit so mis-sized payloads cannot be copied into the bounded queue if they reach this layer.
    /// </para>
    /// <para>
    /// <b>Protocol mapping:</b> Handlers translate <see cref="NntpTransitStorageResult"/> to RFC 4644 responses —
    /// <see cref="NntpTransitStorageResult.Success"/> to <c>235 Article transferred OK</c> (IHAVE) or
    /// <c>239 Article transferred OK</c> (TAKETHIS);
    /// <see cref="NntpTransitStorageResult.QueueFull"/> and
    /// <see cref="NntpTransitStorageResult.ArticleRejected"/> to <c>437 Article rejected</c> (IHAVE) or
    /// <c>439 Transfer failed</c> (TAKETHIS) — permanent rejection, not <c>431</c>/<c>436</c> retry responses.
    /// Failed storage results trigger best-effort HistoryDB release in the command handlers.
    /// </para>
    /// <para>
    /// <b>Observability:</b> Enqueue-time rejections record <see cref="NntpSpoolMetrics.RecordArticleRejected"/> and
    /// <see cref="INntpNewsLog.LogRejected"/>. Successful enqueue does not log here; acceptance is recorded later on the
    /// writer path after spool persistence.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Singleton registered as <see cref="INntpTransitStorage"/> by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// </para>
    /// <para><b>Threading:</b> Safe for concurrent <see cref="TakeThisAsync"/> from multiple transit sessions; enqueue
    /// synchronously completes under <see cref="NntpSpoolWriteQueue"/> locking without async I/O.</para>
    /// </remarks>
    internal sealed partial class NntpSpoolTransitStorage : INntpTransitStorage
    {
        /// <summary>
        /// Minimum interval between queue saturation warning logs.
        /// </summary>
        private static readonly TimeSpan QueueSaturationLogInterval = TimeSpan.FromSeconds(30);
        /// <summary>
        /// Bounded queue receiving spool write items from transit producers.
        /// </summary>
        /// <remarks>
        /// Shared singleton queue also drained by <see cref="NntpSpoolWriterPump"/> workers and observed by
        /// <see cref="NntpSpoolWriterPool"/> for scaling. Enqueue and dequeue paths refresh
        /// <see cref="NntpSpoolMetrics"/> queue gauges.
        /// </remarks>
        private readonly NntpSpoolWriteQueue _queue;

        /// <summary>
        /// Maximum decoded article size copied from <see cref="NntpServerOptions.MaxArtSize"/> at construction.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Values <c>&lt;= 0</c> disable the storage-layer size check, matching body-reader semantics on the socket
        /// path.
        /// </para>
        /// <para>
        /// Compared against <see cref="ReadOnlyMemory{T}.Length"/> (an <see cref="int"/>) using
        /// <c>articleBytes.Length &gt; _maxArtSize</c>; payloads exactly at the limit are accepted.
        /// </para>
        /// </remarks>
        private readonly long _maxArtSize;

        /// <summary>
        /// INN-style news log for enqueue-time rejections before the writer pump runs.
        /// </summary>
        /// <remarks>
        /// Emits minus lines for size and queue-full rejections from <see cref="TakeThisAsync"/>. Does not log successful
        /// enqueues; <see cref="INntpNewsLog.LogAccepted"/> runs after successful spool write on the pump path.
        /// </remarks>
        private readonly INntpNewsLog _newsLog;

        /// <summary>
        /// Spool outcome metrics for enqueue-time rejections and minute throughput snapshots.
        /// </summary>
        /// <remarks>
        /// <see cref="TakeThisAsync"/> calls <see cref="NntpSpoolMetrics.RecordArticleRejected"/> with categories from
        /// <see cref="SpoolArticleRejectionClassifier.ClassifyEnqueueFailure"/> on size and queue-full paths. Successful
        /// enqueue does not increment processed/accepted counters here.
        /// </remarks>
        private readonly NntpSpoolMetrics _metrics;

        /// <summary>
        /// Category logger for rate-limited enqueue saturation warnings and trace enqueue diagnostics.
        /// </summary>
        private readonly ILogger<NntpSpoolTransitStorage> _logger;

        /// <summary>
        /// UTC timestamp of the last queue saturation warning log, used for rate limiting.
        /// </summary>
        private long _lastQueueSaturationLogUtcTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolTransitStorage"/> class.
        /// </summary>
        /// <param name="queue">
        /// Spool write queue used for admission and asynchronous writer drainage. Must be the same singleton registered
        /// for <see cref="NntpSpoolWriterPool"/> and <see cref="NntpSpoolWriterPump"/>.
        /// </param>
        /// <param name="options">
        /// Bound <see cref="NntpServerOptions"/> supplying <see cref="NntpServerOptions.MaxArtSize"/> copied to
        /// <see cref="_maxArtSize"/>. Must align with the NNTPD host body-reader limit.
        /// </param>
        /// <param name="newsLog">INN news log writer for enqueue-time rejection lines.</param>
        /// <param name="metrics">
        /// Spool observability recorder shared with the queue, writer pump, and writer pool.
        /// </param>
        /// <param name="logger">Category logger for enqueue saturation and trace diagnostics.</param>
        /// <remarks>
        /// <see cref="_maxArtSize"/> is frozen at construction; <see cref="IOptionsMonitor{T}"/> changes are not observed.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="queue"/>, <paramref name="options"/>, <paramref name="newsLog"/>, or
        /// <paramref name="metrics"/> is <see langword="null"/>.
        /// </exception>
        public NntpSpoolTransitStorage(
            NntpSpoolWriteQueue queue,
            IOptions<NntpServerOptions> options,
            INntpNewsLog newsLog,
            NntpSpoolMetrics metrics,
            ILogger<NntpSpoolTransitStorage> logger)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(newsLog);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(logger);

            _queue = queue;
            _maxArtSize = options.Value.MaxArtSize;
            _newsLog = newsLog;
            _metrics = metrics;
            _logger = logger;
        }

        /// <summary>
        /// Legacy CHECK hook retained for <see cref="INntpTransitStorage"/> compatibility.
        /// </summary>
        /// <param name="messageId">Message identifier (ignored).</param>
        /// <param name="cancellationToken">Cancellation token (ignored).</param>
        /// <returns>
        /// A synchronously completed <see cref="ValueTask{TResult}"/> returning <see langword="true"/>. Never faults.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Production CHECK duplicate detection uses <see cref="HistoryDB.Abstractions.IHistoryDatabase.CheckAsync"/> in
        /// <see cref="Sockets.Transport.Commands.NntpCmdCheck"/>, not this method.
        /// </para>
        /// <para>
        /// Always returns <see langword="true"/> so legacy callers that still invoke the hook do not block offers. No
        /// I/O, HistoryDB access, or queue interaction occurs. Parameters are intentionally unused.
        /// </para>
        /// </remarks>
        public ValueTask<bool> CheckAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = messageId;
            _ = cancellationToken;
            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// IHAVE offer hook retained for <see cref="INntpTransitStorage"/> compatibility.
        /// </summary>
        /// <param name="messageId">Message identifier (ignored).</param>
        /// <param name="cancellationToken">Cancellation token (ignored).</param>
        /// <returns>
        /// A synchronously completed <see cref="ValueTask{TResult}"/> returning <see langword="true"/> so legacy callers
        /// may reply <c>335 Send article</c>. Never faults.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Production IHAVE duplicate filtering and <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryRecordAsync"/> run
        /// in <see cref="Sockets.Transport.Commands.NntpCmdIHave"/> before the article body is read. This method does not
        /// inspect <paramref name="messageId"/> or influence offer outcomes.
        /// </para>
        /// <para>No I/O or queue interaction occurs. Parameters are intentionally unused.</para>
        /// </remarks>
        public ValueTask<bool> IHaveAsync(string messageId, CancellationToken cancellationToken)
        {
            _ = messageId;
            _ = cancellationToken;
            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// Copies a transit article into the spool write queue when size and queue admission budgets allow.
        /// </summary>
        /// <param name="messageId">
        /// RFC 5322-style message identifier for the article. Must be non-empty; wire-level syntax validation is the
        /// caller's responsibility before HistoryDB reservation.
        /// </param>
        /// <param name="articleBytes">
        /// Raw dot-stuffed article bytes as decoded on the socket path — headers, header/body separator, and
        /// optional body. Header-only articles (headers plus blank line, no body lines) have non-zero length. On the
        /// success and queue-full paths this method copies the payload via <see cref="ReadOnlyMemory{T}.ToArray"/>
        /// before enqueue.
        /// </param>
        /// <param name="origin">
        /// Peer identity and UTC reception timestamp from the transit session. Converted to
        /// <see cref="NntpSpoolArticleOrigin"/> and stored on the queued <see cref="NntpSpoolWriteItem"/> for metrics,
        /// news log, and optional spamd synthesis.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancellation token (currently unused; enqueue is fully synchronous and does not observe cancellation).
        /// </param>
        /// <returns>
        /// A synchronously completed <see cref="ValueTask{TResult}"/> with one of:
        /// <see cref="NntpTransitStorageResult.Success"/> when queued;
        /// <see cref="NntpTransitStorageResult.ArticleRejected"/> when
        /// <paramref name="articleBytes"/> exceeds <see cref="NntpServerOptions.MaxArtSize"/> and the limit is enabled;
        /// <see cref="NntpTransitStorageResult.QueueFull"/> when <see cref="NntpSpoolWriteQueue.TryEnqueue"/> rejects
        /// admission after digest computation and array copy.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para><b>Size rejection:</b> When <see cref="_maxArtSize"/> is positive and
        /// <c>articleBytes.Length &gt; _maxArtSize</c>, returns <see cref="NntpTransitStorageResult.ArticleRejected"/>
        /// without digest, heap copy, or enqueue. Records rejection metrics and a news minus line using
        /// <paramref name="articleBytes"/> as a span view.</para>
        /// <para><b>Queue rejection:</b> <see cref="NntpTransitStorageResult.QueueFull"/> indicates spool item-count or
        /// byte-budget saturation, not malformed content. Occurs after digest and <see cref="NntpSpoolWriteItem"/> array
        /// allocation; the constructed item is discarded when <see cref="NntpSpoolWriteQueue.TryEnqueue"/> returns
        /// <see langword="false"/>. Handlers use the same permanent-failure response codes as
        /// <see cref="NntpTransitStorageResult.ArticleRejected"/> at the wire layer.</para>
        /// <para>
        /// <b>Success path:</b> Builds a <see cref="NntpSpoolWriteItem"/> with
        /// <see cref="HistoryKeyEncoder.EncodeHexLower(string)"/> digest so writer pumps avoid re-hashing on the hot
        /// path. The <see cref="ReadOnlyMemory{T}.ToArray"/> copy is the dominant memory-allocation cost. Writer pumps
        /// dequeue asynchronously; no HistoryDB release is performed here on success.
        /// </para>
        /// <para>
        /// History reservation release on storage failure is not performed here;
        /// <see cref="Sockets.Transport.Commands.NntpCmdTakethis"/> and
        /// <see cref="Sockets.Transport.Commands.NntpCmdIHave"/> call
        /// <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/> when the result is not
        /// <see cref="NntpTransitStorageResult.Success"/>.
        /// </para>
        /// </remarks>
        public ValueTask<NntpTransitStorageResult> TakeThisAsync(
            string messageId,
            ReadOnlyMemory<byte> articleBytes,
            NntpTransitArticleOrigin origin,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(messageId);

            NntpSpoolArticleOrigin spoolOrigin = NntpSpoolArticleOrigin.FromTransit(origin);
            if (_maxArtSize > 0 && articleBytes.Length > _maxArtSize)
            {
                string reason = $"Article exceeds local limit of {_maxArtSize} bytes";
                _metrics.RecordArticleRejected(
                    spoolOrigin,
                    articleBytes.Span,
                    SpoolArticleRejectionClassifier.ClassifyEnqueueFailure(reason));
                _newsLog.LogRejected(
                    messageId,
                    spoolOrigin,
                    articleBytes.Span,
                    reason);
                return ValueTask.FromResult(NntpTransitStorageResult.ArticleRejected);
            }

            string digestHex = HistoryKeyEncoder.EncodeHexLower(messageId);
            NntpSpoolWriteItem item = new(
                messageId,
                articleBytes.ToArray(),
                digestHex,
                spoolOrigin);

            bool queued = _queue.TryEnqueue(item);
            if (!queued)
            {
                const string Reason = "Queue full";
                _metrics.RecordArticleRejected(
                    spoolOrigin,
                    articleBytes.Span,
                    SpoolArticleRejectionClassifier.ClassifyEnqueueFailure(Reason));
                _newsLog.LogRejected(messageId, spoolOrigin, articleBytes.Span, Reason);
                MaybeLogQueueSaturation();
                return ValueTask.FromResult(NntpTransitStorageResult.QueueFull);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                LogEnqueueAccepted(_logger, messageId, item.ArticleBytes.Length);
            }

            return ValueTask.FromResult(NntpTransitStorageResult.Success);
        }

        /// <summary>
        /// Emits a rate-limited queue saturation warning and counter when enqueue rejects spike.
        /// </summary>
        /// <remarks>
        /// Uses <see cref="Interlocked.CompareExchange(ref long, long, long)"/> on
        /// <see cref="_lastQueueSaturationLogUtcTicks"/> so concurrent transit sessions emit at most one warning per
        /// <see cref="QueueSaturationLogInterval"/> window.
        /// </remarks>
        private void MaybeLogQueueSaturation()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            long lastTicks = Volatile.Read(ref _lastQueueSaturationLogUtcTicks);
            if (nowTicks - lastTicks < QueueSaturationLogInterval.Ticks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastQueueSaturationLogUtcTicks, nowTicks, lastTicks) != lastTicks)
            {
                return;
            }

            _metrics.RecordQueueSaturationLog();
            LogQueueSaturation(_logger, _queue.Depth, _queue.Capacity);
        }
    }
}
