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
    /// <see cref="NntpSpoolWriteQueue"/> for asynchronous spool disk write.
    /// </summary>
    /// <remarks>
    /// <para><b>Role:</b> NNTPD command handlers invoke <see cref="TakeThisAsync"/> after HistoryDB records the
    /// message-id and the dot-stuffed body is decoded. This adapter is the boundary between protocol I/O and the
    /// bounded in-memory spool pipeline consumed by <see cref="NntpSpoolWriterPump"/>.</para>
    /// <para>
    /// <see cref="CheckAsync"/> and <see cref="IHaveAsync"/> always return <see langword="true"/> because duplicate
    /// filtering and offer policy are handled by <see cref="HistoryDB.Abstractions.IHistoryDatabase"/> and socket-layer
    /// command handlers before body transfer. This type only persists accepted bodies via <see cref="TakeThisAsync"/>.
    /// </para>
    /// <para>
    /// <b>Admission ordering:</b> <see cref="TakeThisAsync"/> checks
    /// <see cref="NntpServerOptions.MaxArtSize"/> before digest generation or array copy. When the spool queue is
    /// already full, digest and copy still run before <see cref="NntpSpoolWriteQueue.TryEnqueue"/> fails — BLAKE3 over
    /// a message-id is negligible relative to article handling, so a pre-enqueue <c>CanAccept</c> fast path is
    /// intentionally omitted.
    /// </para>
    /// <para>
    /// <b>Defense in depth:</b> Command handlers enforce <see cref="NntpServerOptions.MaxArtSize"/> while reading
    /// dot-stuffed bodies via <see cref="Sockets.Transport.NntpArticleBodyReader"/>. Storage repeats the limit so
    /// mis-sized payloads cannot be copied into the bounded queue if they reach this layer.
    /// </para>
    /// <para>
    /// <b>Protocol mapping:</b> Callers translate <see cref="NntpTransitStorageResult"/> to RFC 4644 responses —
    /// <see cref="NntpTransitStorageResult.Success"/> to <c>235</c>/<c>239</c>,
    /// <see cref="NntpTransitStorageResult.QueueFull"/> to <c>437</c>/<c>439</c>, and
    /// <see cref="NntpTransitStorageResult.ArticleRejected"/> to <c>437</c>/<c>439</c> (permanent rejection, not
    /// <c>431</c>/<c>436</c> retry responses).
    /// </para>
    /// <para>
    /// <b>Registration:</b> Registered as
    /// <see cref="INntpTransitStorage"/> by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// </para>
    /// <para><b>Threading:</b> Singleton; safe for concurrent <see cref="TakeThisAsync"/> from multiple transit sessions.</para>
    /// </remarks>
    internal sealed class NntpSpoolTransitStorage : INntpTransitStorage
    {
        /// <summary>
        /// Bounded queue receiving spool write items from transit producers.
        /// </summary>
        /// <remarks>
        /// Shared singleton queue also observed by <see cref="NntpSpoolWriterPool"/> for scaling and by
        /// <see cref="Metrics.NntpSpoolMetrics"/> on enqueue and dequeue paths.
        /// </remarks>
        private readonly NntpSpoolWriteQueue _queue;

        /// <summary>
        /// Maximum decoded article size copied from <see cref="NntpServerOptions.MaxArtSize"/> at construction.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Values <c>&lt;= 0</c> disable the storage-layer size check, matching
        /// <see cref="Sockets.Transport.NntpArticleBodyReader"/> semantics.
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
        private readonly INntpNewsLog _newsLog;

        /// <summary>
        /// Spool outcome metrics for enqueue-time rejections and minute throughput snapshots.
        /// </summary>
        private readonly NntpSpoolMetrics _metrics;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolTransitStorage"/> class.
        /// </summary>
        /// <param name="queue">Spool write queue used for admission and asynchronous writer drainage.</param>
        /// <param name="options">
        /// Bound server options supplying <see cref="NntpServerOptions.MaxArtSize"/>. Must be the same
        /// <see cref="NntpServerOptions"/> instance bound by the NNTPD host so storage and body-reader limits agree.
        /// </param>
        /// <param name="newsLog">INN news log writer for enqueue rejections.</param>
        /// <param name="metrics">Spool outcome metrics recorder shared with writer pumps.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="queue"/>, <paramref name="options"/>, <paramref name="newsLog"/>, or
        /// <paramref name="metrics"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <see cref="_maxArtSize"/> is frozen at construction; options monitor changes are not observed.
        /// </remarks>
        public NntpSpoolTransitStorage(
            NntpSpoolWriteQueue queue,
            IOptions<NntpServerOptions> options,
            INntpNewsLog newsLog,
            NntpSpoolMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(newsLog);
            ArgumentNullException.ThrowIfNull(metrics);

            _queue = queue;
            _maxArtSize = options.Value.MaxArtSize;
            _newsLog = newsLog;
            _metrics = metrics;
        }

        /// <summary>
        /// Legacy CHECK hook retained for <see cref="INntpTransitStorage"/> compatibility.
        /// </summary>
        /// <param name="messageId">Message identifier (ignored).</param>
        /// <param name="cancellationToken">Cancellation token (ignored).</param>
        /// <returns>
        /// A synchronously completed <see cref="ValueTask{TResult}"/> returning <see langword="true"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Production CHECK duplicate detection uses <see cref="HistoryDB.Abstractions.IHistoryDatabase.CheckAsync"/>
        /// in the socket command handler, not this method.
        /// </para>
        /// <para>
        /// Always returns <see langword="true"/> so legacy callers that still invoke the hook do not block offers.
        /// No I/O or queue interaction occurs.
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
        /// A synchronously completed <see cref="ValueTask{TResult}"/> returning <see langword="true"/> so the IHAVE
        /// handler may reply <c>335 Send article</c>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Duplicate filtering and <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryRecordAsync"/> run in the
        /// socket IHAVE handler before the article body is read. This method does not inspect
        /// <paramref name="messageId"/> or influence offer outcomes.
        /// </para>
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
        /// RFC 5322-style message identifier for the article. Must be non-empty; syntax validation is the caller's
        /// responsibility.
        /// </param>
        /// <param name="articleBytes">
        /// Raw dot-stuffed article bytes as decoded by <see cref="Sockets.Transport.NntpArticleBodyReader"/> — headers,
        /// header/body separator, and optional body. Header-only articles (headers plus blank line, no body lines) have
        /// non-zero length. This method copies the payload via <see cref="ReadOnlyMemory{T}.ToArray"/> before enqueue.
        /// </param>
        /// <param name="origin">Peer identity and UTC reception timestamp for downstream SpamAssassin scan synthesis.</param>
        /// <param name="cancellationToken">Cancellation token (currently unused; enqueue is synchronous).</param>
        /// <returns>
        /// A synchronously completed <see cref="ValueTask{TResult}"/> with one of:
        /// <see cref="NntpTransitStorageResult.Success"/> when queued;
        /// <see cref="NntpTransitStorageResult.ArticleRejected"/> when
        /// <paramref name="articleBytes"/> exceeds <see cref="NntpServerOptions.MaxArtSize"/> (and the limit is enabled);
        /// <see cref="NntpTransitStorageResult.QueueFull"/> when <see cref="NntpSpoolWriteQueue.TryEnqueue"/> rejects
        /// admission.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> is null or empty.</exception>
        /// <remarks>
        /// <para><b>Size rejection:</b> When <see cref="_maxArtSize"/> is positive and
        /// <c>articleBytes.Length &gt; _maxArtSize</c>, returns <see cref="NntpTransitStorageResult.ArticleRejected"/>
        /// without digest, copy, or enqueue. Handlers map this to <c>437 Article rejected</c> (IHAVE) or
        /// <c>439 Transfer failed</c> (TAKETHIS).</para>
        /// <para><b>Queue rejection:</b> <see cref="NntpTransitStorageResult.QueueFull"/> indicates spool item-count or
        /// byte-budget saturation, not malformed content. Handlers use the same permanent-failure response codes as
        /// <see cref="NntpTransitStorageResult.ArticleRejected"/> at the wire layer.</para>
        /// <para>
        /// <b>Success path:</b> Builds a <see cref="NntpSpoolWriteItem"/> with a precomputed
        /// <see cref="HistoryKeyEncoder.EncodeHexLower(string)"/> digest so writer pumps avoid re-hashing on the hot
        /// path. Digest is computed before <c>articleBytes.ToArray()</c>; the array copy remains the dominant
        /// memory-allocation cost on this path.
        /// </para>
        /// <para>
        /// History reservation release on rejection is not performed here; callers own post-failure HistoryDB policy.
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
                const string reason = "Queue full";
                _metrics.RecordArticleRejected(
                    spoolOrigin,
                    articleBytes.Span,
                    SpoolArticleRejectionClassifier.ClassifyEnqueueFailure(reason));
                _newsLog.LogRejected(messageId, spoolOrigin, articleBytes.Span, reason);
                return ValueTask.FromResult(NntpTransitStorageResult.QueueFull);
            }

            return ValueTask.FromResult(NntpTransitStorageResult.Success);
        }
    }
}
