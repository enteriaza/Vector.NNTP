// <copyright file="NntpSpoolWriterPump.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: queue drain worker for preprocessing and atomic spool writes.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Diagnostics;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Drains queued transit articles, preprocesses and postprocesses them, and atomically writes a single payload file per Message-ID.
    /// </summary>
    /// <remarks>
    /// <para><b>Per-item pipeline:</b></para>
    /// <list type="number">
    /// <item><description>Dequeue a <see cref="NntpSpoolWriteItem"/> from <see cref="NntpSpoolWriteQueue.Reader"/>.</description></item>
    /// <item>
    /// <description>
    /// Run fast header validation and optional path-hop mutation via <see cref="ArticleSpoolPreprocessor"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Run deep header semantics, Message-ID, date, and filter style checks via
    /// <see cref="ArticleSpoolPostprocessor"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Ensure the digest fan-out leaf directory exists (cached), then write
    /// <c>Incoming/{aa}/{bb}/{digest}</c> with <see cref="FileIOUtilities.AtomicWriteAsync"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// On preprocess, postprocess, or write failure, release the HistoryDB reservation with
    /// <see cref="IHistoryDatabase.TryReleaseAsync"/> so peers may re-offer the article.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// On successful <see cref="FileIOUtilities.AtomicWriteAsync"/>, retain the HistoryDB reservation — it transitions
    /// with ownership to the persisted spool article; there is no release on the success path.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Decrement queue depth and byte gauges via <see cref="NntpSpoolWriteQueue.NotifyDequeued(int)"/> in a
    /// <c>finally</c> block so metrics include preprocessing and I/O still in flight.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <see cref="NntpSpoolWriterPool"/> may run multiple concurrent <see cref="RunAsync"/> tasks against one pump
    /// singleton. The channel reader serializes dequeue; <see cref="_knownDirectories"/> coordinates directory
    /// creation across workers.
    /// </para>
    /// <para>
    /// <b>History reservation invariant:</b> Reservations are held from CHECK acceptance through successful spool
    /// persistence. <see cref="TryReleaseHistoryReservationAsync"/> runs only on preprocess, postprocess, write failure,
    /// or worker cancellation during an in-flight write. A completed atomic write keeps the reservation so duplicate
    /// CHECK rejections remain correct for the persisted article.
    /// </para>
    /// <para>
    /// Source-generated log helpers live in the logging partial class file (EventIds 1-5).
    /// </para>
    /// </remarks>
    internal sealed partial class NntpSpoolWriterPump
    {
        /// <summary>
        /// Bounded spool queue supplying the channel reader and dequeue metric updates.
        /// </summary>
        private readonly NntpSpoolWriteQueue _queue;

        /// <summary>
        /// Preprocessor that validates NNTP header syntax and applies <c>PathAppend</c> hop mutation before postprocessing.
        /// </summary>
        private readonly ArticleSpoolPreprocessor _preprocessor;

        /// <summary>
        /// Postprocessor that validates header semantics, Message-ID, date headers, and filter style rules before disk write.
        /// </summary>
        private readonly ArticleSpoolPostprocessor _postprocessor;

        /// <summary>
        /// History store used to release digest reservations when spool preprocessing, postprocessing, or persistence fails.
        /// </summary>
        private readonly IHistoryDatabase _historyDatabase;

        /// <summary>
        /// Spool queue and writer counters updated on preprocess/postprocess failure, write success/failure, and history release faults.
        /// </summary>
        private readonly NntpSpoolMetrics _metrics;

        /// <summary>
        /// Logger consumed by source-generated <c>Log*</c> methods on the logging partial.
        /// </summary>
        private readonly ILogger<NntpSpoolWriterPump> _logger;

        /// <summary>
        /// Absolute spool root resolved once from <see cref="NntpServerOptions.SpoolDir"/> at construction.
        /// </summary>
        private readonly string _spoolDirectory;

        /// <summary>
        /// Leaf spool directories already created on this host (for example <c>Incoming/ab/cd</c>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Digest fan-out yields at most 65,536 distinct leaf paths. <see cref="EnsureArticleDirectoryExists"/> uses
        /// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> so each path triggers at most one
        /// <see cref="Directory.CreateDirectory(string)"/> call across all writer workers sharing this pump instance.
        /// </para>
        /// <para>
        /// Keys use <see cref="StringComparer.Ordinal"/> because
        /// <see cref="Diagnostics.SpoolDirectoryUtilities.GetArticleFilePath"/> validates lowercase hexadecimal digests
        /// only; fan-out directory names are therefore case-stable on Linux as well as Windows.
        /// </para>
        /// </remarks>
        private readonly ConcurrentDictionary<string, byte> _knownDirectories = new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolWriterPump"/> class.
        /// </summary>
        /// <param name="queue">Bounded in-memory queue written by transit socket threads.</param>
        /// <param name="preprocessor">Fast header syntax validator and path mutator for queued article bytes.</param>
        /// <param name="postprocessor">Deep header and filter validator for preprocessed article bytes.</param>
        /// <param name="historyDatabase">History database for releasing failed spool reservations.</param>
        /// <param name="metrics">Spool observability recorder shared with the queue and writer pool.</param>
        /// <param name="options">Bound <see cref="NntpServerOptions"/> supplying spool root and <c>PathAppend</c>.</param>
        /// <param name="logger">Category logger for writer pump diagnostics.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when any dependency parameter is <see langword="null"/>.
        /// </exception>
        public NntpSpoolWriterPump(
            NntpSpoolWriteQueue queue,
            ArticleSpoolPreprocessor preprocessor,
            ArticleSpoolPostprocessor postprocessor,
            IHistoryDatabase historyDatabase,
            NntpSpoolMetrics metrics,
            IOptions<NntpServerOptions> options,
            ILogger<NntpSpoolWriterPump> logger)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentNullException.ThrowIfNull(preprocessor);
            ArgumentNullException.ThrowIfNull(postprocessor);
            ArgumentNullException.ThrowIfNull(historyDatabase);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);

            _queue = queue;
            _preprocessor = preprocessor;
            _postprocessor = postprocessor;
            _historyDatabase = historyDatabase;
            _metrics = metrics;
            _logger = logger;
            _spoolDirectory = SpoolDirectoryUtilities.ResolveSpoolDirectory(options.Value);
        }

        /// <summary>
        /// Runs the worker loop until the queue completes, the worker token is canceled, or a fatal read error occurs.
        /// </summary>
        /// <param name="cancellationToken">
        /// Per-worker token linked to host shutdown and pool scale-down. Passed to queue reads and atomic writes.
        /// </param>
        /// <returns>
        /// A task that completes normally when the reader is closed or cancellation is requested; does not fault on
        /// expected worker cancellation.
        /// </returns>
        /// <remarks>
        /// <para><b>Exit paths:</b></para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// <see cref="ChannelClosedException"/> after <see cref="NntpSpoolWriteQueue.Complete"/> — normal shutdown drain.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <see cref="OperationCanceledException"/> on queue read when <paramref name="cancellationToken"/> is canceled —
        /// scale-down or host stop while idle.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <see cref="OperationCanceledException"/> during atomic write — history is released, dequeue metrics are
        /// updated in <c>finally</c>, then the method returns without faulting the task.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Preprocess, postprocess, and write failures are logged, release history best-effort, and continue the loop.
        /// Unexpected write exceptions do not terminate the worker.
        /// </para>
        /// </remarks>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                NntpSpoolWriteItem item;
                try
                {
                    item = await _queue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    ArticleSpoolPreprocessResult preprocessResult = await _preprocessor
                        .PreprocessAsync(item.MessageId, item.ArticleBytes)
                        .ConfigureAwait(false);
                    if (!preprocessResult.Success)
                    {
                        _metrics.RecordPreprocessFailure();
                        this.LogPreprocessFailed(item.MessageId, preprocessResult.FailureReason);
                        await TryReleaseHistoryReservationAsync(item.MessageId).ConfigureAwait(false);
                        continue;
                    }

                    ArticleSpoolPostprocessResult postprocessResult = await _postprocessor
                        .PostprocessAsync(item, preprocessResult.ArticleBytes, cancellationToken)
                        .ConfigureAwait(false);
                    if (!postprocessResult.Success)
                    {
                        _metrics.RecordPostprocessFailure();
                        this.LogPostprocessFailed(item.MessageId, postprocessResult.FailureReason);
                        await TryReleaseHistoryReservationAsync(item.MessageId).ConfigureAwait(false);
                        continue;
                    }

                    _metrics.RecordArticleTypes(postprocessResult.ArticleType);

                    try
                    {
                        string articlePath = SpoolDirectoryUtilities.GetArticleFilePath(_spoolDirectory, item.MessageIdDigestHex);
                        string? articleDirectory = Path.GetDirectoryName(articlePath);
                        if (!string.IsNullOrEmpty(articleDirectory))
                        {
                            this.EnsureArticleDirectoryExists(articleDirectory);
                        }

                        await FileIOUtilities.AtomicWriteAsync(articlePath, postprocessResult.ArticleBytes, cancellationToken).ConfigureAwait(false);
                        _metrics.RecordWriteSuccess(postprocessResult.ArticleBytes.Length);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        await TryReleaseHistoryReservationAsync(item.MessageId).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _metrics.RecordWriteFailure();
                        this.LogWriteFailed(ex, item.MessageId, item.MessageIdDigestHex);
                        await TryReleaseHistoryReservationAsync(item.MessageId).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _queue.NotifyDequeued(item.ArticleBytes.Length);
                }
            }
        }

        /// <summary>
        /// Ensures a digest fan-out leaf directory exists, calling <see cref="Directory.CreateDirectory(string)"/> at most once per path.
        /// </summary>
        /// <param name="articleDirectory">
        /// Absolute directory path (for example <c>{SpoolRoot}/Incoming/ab/cd</c>) containing the article payload file.
        /// </param>
        /// <remarks>
        /// <para>
        /// Concurrent workers may race on <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/>; losers skip creation
        /// because the winner already created the directory. <see cref="Directory.CreateDirectory(string)"/> is idempotent
        /// when the directory already exists.
        /// </para>
        /// <para>
        /// When <see cref="Directory.CreateDirectory(string)"/> throws after a successful <c>TryAdd</c>, the path is
        /// removed from <see cref="_knownDirectories"/> before the exception propagates so a later write can retry
        /// directory creation after a transient I/O or permission failure.
        /// </para>
        /// </remarks>
        /// <exception cref="IOException">
        /// Propagated when directory creation fails due to filesystem or permission errors.
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// Propagated when the process lacks permission to create the leaf directory.
        /// </exception>
        private void EnsureArticleDirectoryExists(string articleDirectory)
        {
            if (_knownDirectories.TryAdd(articleDirectory, 0))
            {
                try
                {
                    Directory.CreateDirectory(articleDirectory);
                }
                catch
                {
                    _ = _knownDirectories.TryRemove(articleDirectory, out _);
                    throw;
                }
            }
        }

        /// <summary>
        /// Releases a HistoryDB reservation after spool preprocessing, postprocessing, or write failure.
        /// </summary>
        /// <param name="messageId">NNTP Message-ID whose digest reservation should be removed.</param>
        /// <returns>A task that completes after the release attempt and any failure logging.</returns>
        /// <remarks>
        /// <para>
        /// Uses <see cref="CancellationToken.None"/> so cleanup completes even when the worker token is already canceled.
        /// Invoked only from failure and cancellation paths — successful writes intentionally retain the reservation.
        /// </para>
        /// <para>
        /// <see cref="HistoryReleaseResult.Released"/> and <see cref="HistoryReleaseResult.NotFound"/> are treated as
        /// success. <see cref="HistoryReleaseResult.TryAgainLater"/> and <see cref="HistoryReleaseResult.Unavailable"/>
        /// increment failure metrics and emit warning logs. Exceptions are logged and swallowed so the worker loop
        /// continues.
        /// </para>
        /// </remarks>
        private async Task TryReleaseHistoryReservationAsync(string messageId)
        {
            try
            {
                HistoryReleaseResult releaseResult = await _historyDatabase
                    .TryReleaseAsync(messageId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (releaseResult is not (HistoryReleaseResult.Released or HistoryReleaseResult.NotFound))
                {
                    _metrics.RecordHistoryReleaseFailure();
                    this.LogHistoryReleaseOutcome(releaseResult, messageId);
                }
            }
            catch (Exception ex)
            {
                _metrics.RecordHistoryReleaseFailure();
                this.LogHistoryReleaseFailed(ex, messageId, ex.GetType().Name);
            }
        }
    }
}
