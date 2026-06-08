// <copyright file="NntpSpoolWriterPump.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: queue drain worker for preprocessing and atomic spool writes.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Vector.NNTP.Articles.Classification;
using Vector.NNTP.Articles.Diagnostics;
using Vector.NNTP.Articles.Logging;
using Vector.NNTP.Articles.Metrics;
using Vector.NNTP.Articles.Processing;
using Vector.NNTP.Articles.Telemetry;
using Vector.NNTP.HistoryDB.Abstractions;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Drains queued transit articles, preprocesses and postprocesses them, and atomically writes one spool payload file
    /// per Message-ID digest under the configured spool root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Singleton worker engine registered by
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// <see cref="NntpSpoolWriterPool"/> starts one or more concurrent <see cref="RunAsync"/> tasks against the same
    /// instance; each task competes for <see cref="NntpSpoolWriteQueue.Reader"/> items and shares directory-creation
    /// state in <see cref="_knownDirectories"/>.
    /// </para>
    /// <para><b>Per-item pipeline:</b></para>
    /// <list type="number">
    /// <item><description>Dequeue a <see cref="NntpSpoolWriteItem"/> from <see cref="NntpSpoolWriteQueue.Reader"/>.</description></item>
    /// <item>
    /// <description>
    /// Run fast header syntax validation and optional <c>PathAppend</c> hop mutation via
    /// <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> (synchronous work today; no worker cancellation token).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Run deep header semantics, Message-ID and date validation, filter style rules, optional yEnc CRC, and optional
    /// SpamAssassin checks via <see cref="ArticleSpoolPostprocessor.PostprocessAsync"/> (honors the worker
    /// <see cref="CancellationToken"/>).
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
    /// On successful write, record acceptance metrics, emit <see cref="INntpNewsLog.LogAccepted"/>, optionally
    /// <see cref="INntpNewsLog.LogCancelProcessed"/> for cancel articles, and commit history with
    /// <see cref="TryCommitHistoryReservationAsync"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// On preprocess, postprocess, or write failure, record rejection metrics, emit
    /// <see cref="INntpNewsLog.LogRejected"/>, and release the HistoryDB reservation with
    /// <see cref="TryReleaseHistoryReservationAsync"/> so peers may re-offer the article.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Decrement queue depth and byte gauges via <see cref="NntpSpoolWriteQueue.NotifyDequeued(int)"/> in a
    /// <c>finally</c> block using the original enqueued <see cref="NntpSpoolWriteItem.ArticleBytes"/> length so metrics
    /// include preprocessing and I/O still in flight.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Concurrency:</b> The bounded channel reader serializes dequeue per competing worker, but postprocess, disk I/O,
    /// and history calls for different items may overlap across workers. <see cref="_knownDirectories"/> coordinates leaf
    /// directory creation so each fan-out path triggers at most one <see cref="Directory.CreateDirectory(string)"/> per
    /// pump instance.
    /// </para>
    /// <para>
    /// <b>History reservation invariant:</b> TAKETHIS/IHAVE call <see cref="IHistoryDatabase.TryRecordAsync"/> before body
    /// transfer; the pump commits again after successful spool persistence so history aligns with news-log <c>+</c>
    /// lines. <see cref="TryReleaseHistoryReservationAsync"/> runs only on preprocess, postprocess, write failure, or
    /// worker cancellation during an in-flight atomic write. A completed atomic write keeps the reservation so duplicate
    /// <c>CHECK</c> rejections remain correct for the persisted article.
    /// </para>
    /// <para>
    /// <b>Observability:</b> Per-article failure logs (EventIds 1-7) live in the logging partial
    /// (<c>NntpSpoolWriterPump.Logging.cs</c>). Acceptance and rejection rollups use <see cref="NntpSpoolMetrics"/> and
    /// <see cref="INntpNewsLog"/>; successful spool writes do not emit Information-level pump logs.
    /// </para>
    /// <para><b>Threading:</b> Instance fields are read-only after construction and safe for concurrent
    /// <see cref="RunAsync"/> workers without external locking on the pump itself.</para>
    /// </remarks>
    internal sealed partial class NntpSpoolWriterPump
    {
        /// <summary>
        /// Bounded spool queue supplying the channel reader and dequeue metric updates.
        /// </summary>
        /// <remarks>
        /// Shared singleton written by transit socket threads via <see cref="NntpSpoolTransitStorage"/> and read by all
        /// <see cref="NntpSpoolWriterPool"/> workers. <see cref="NntpSpoolWriteQueue.NotifyDequeued(int)"/> is invoked
        /// from <see cref="RunAsync"/> <c>finally</c> so scaling policy depth includes in-flight items.
        /// </remarks>
        private readonly NntpSpoolWriteQueue _queue;

        /// <summary>
        /// Preprocessor that validates NNTP header syntax and applies <c>PathAppend</c> hop mutation before postprocessing.
        /// </summary>
        /// <remarks>
        /// Invoked synchronously through <see cref="ArticleSpoolPreprocessor.PreprocessAsync"/> for every dequeued item.
        /// Failures short-circuit before postprocess and disk I/O.
        /// </remarks>
        private readonly ArticleSpoolPreprocessor _preprocessor;

        /// <summary>
        /// Postprocessor that validates header semantics, Message-ID, date headers, filter style rules, yEnc CRC, and spam policy.
        /// </summary>
        /// <remarks>
        /// Invoked from <see cref="RunAsync"/> with the worker <see cref="CancellationToken"/>. May perform asynchronous
        /// SpamAssassin I/O on eligible articles. Failures short-circuit before disk I/O.
        /// </remarks>
        private readonly ArticleSpoolPostprocessor _postprocessor;

        /// <summary>
        /// History store used to release digest reservations on failure and commit reservations after successful spool writes.
        /// </summary>
        /// <remarks>
        /// <see cref="TryReleaseHistoryReservationAsync"/> and <see cref="TryCommitHistoryReservationAsync"/> both call
        /// with <see cref="CancellationToken.None"/> so history cleanup completes even when the worker token is canceled.
        /// </remarks>
        private readonly IHistoryDatabase _historyDatabase;

        /// <summary>
        /// Spool observability recorder updated on preprocess/postprocess failure, write success/failure, article types, and history faults.
        /// </summary>
        /// <remarks>
        /// Shared singleton with <see cref="NntpSpoolWriteQueue"/> and <see cref="NntpSpoolWriterPool"/>. Rejection
        /// categories are classified through <see cref="SpoolArticleRejectionClassifier"/> before
        /// <see cref="NntpSpoolMetrics.RecordArticleRejected"/>.
        /// </remarks>
        private readonly NntpSpoolMetrics _metrics;

        /// <summary>
        /// Category logger passed to source-generated <c>Log*</c> helpers on the logging partial.
        /// </summary>
        /// <remarks>
        /// Emits Warning and Error entries for preprocess, postprocess, write, and history repair paths only.
        /// </remarks>
        private readonly ILogger<NntpSpoolWriterPump> _logger;

        /// <summary>
        /// INN-style news log writer for spool acceptance, rejection, and cancel-processed lines.
        /// </summary>
        /// <remarks>
        /// <see cref="INntpNewsLog.LogAccepted"/> and <see cref="INntpNewsLog.LogCancelProcessed"/> run only after
        /// successful atomic write. <see cref="INntpNewsLog.LogRejected"/> runs on preprocess, postprocess, and write
        /// failure paths before history release.
        /// </remarks>
        private readonly INntpNewsLog _newsLog;

        /// <summary>
        /// Absolute spool root directory resolved once from <see cref="NntpServerOptions.SpoolDir"/> at construction.
        /// </summary>
        /// <remarks>
        /// Passed to <see cref="SpoolDirectoryUtilities.GetArticleFilePath"/> with each item's
        /// <see cref="NntpSpoolWriteItem.MessageIdDigestHex"/> to build <c>Incoming/{aa}/{bb}/{digest}</c> paths.
        /// </remarks>
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
        /// <see cref="SpoolDirectoryUtilities.GetArticleFilePath"/> validates lowercase hexadecimal digests
        /// only; fan-out directory names are therefore case-stable on Linux as well as Windows.
        /// </para>
        /// <para>
        /// Values are unused placeholders; only key presence matters.
        /// </para>
        /// </remarks>
        private readonly ConcurrentDictionary<string, byte> _knownDirectories = new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSpoolWriterPump"/> class.
        /// </summary>
        /// <param name="queue">
        /// Bounded in-memory queue written by transit socket threads. Must be the same singleton instance registered for
        /// <see cref="NntpSpoolTransitStorage"/> and <see cref="NntpSpoolWriterPool"/>.
        /// </param>
        /// <param name="preprocessor">
        /// Fast header syntax validator and <c>PathAppend</c> path mutator for raw queued article bytes.
        /// </param>
        /// <param name="postprocessor">
        /// Deep header, filter, yEnc, and spam validator producing the byte payload written to disk on success.
        /// </param>
        /// <param name="historyDatabase">
        /// History database for releasing failed reservations and committing digests after successful spool persistence.
        /// </param>
        /// <param name="metrics">
        /// Spool observability recorder shared with the queue, transit storage, and writer pool.
        /// </param>
        /// <param name="options">
        /// Bound <see cref="NntpServerOptions"/> supplying <see cref="NntpServerOptions.SpoolDir"/> resolved to
        /// <see cref="_spoolDirectory"/> via <see cref="SpoolDirectoryUtilities.ResolveSpoolDirectory"/>.
        /// </param>
        /// <param name="newsLog">
        /// INN news log writer for acceptance, rejection, and cancel-processed events on the writer path.
        /// </param>
        /// <param name="logger">
        /// Category logger for source-generated pump failure diagnostics (EventIds 1-7).
        /// </param>
        /// <remarks>
        /// Resolves and caches the spool root once; invalid <see cref="NntpServerOptions.SpoolDir"/> configuration fails
        /// at construction time through <see cref="SpoolDirectoryUtilities.ResolveSpoolDirectory"/>.
        /// </remarks>
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
            INntpNewsLog newsLog,
            ILogger<NntpSpoolWriterPump> logger)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentNullException.ThrowIfNull(preprocessor);
            ArgumentNullException.ThrowIfNull(postprocessor);
            ArgumentNullException.ThrowIfNull(historyDatabase);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(newsLog);
            ArgumentNullException.ThrowIfNull(logger);

            _queue = queue;
            _preprocessor = preprocessor;
            _postprocessor = postprocessor;
            _historyDatabase = historyDatabase;
            _metrics = metrics;
            _newsLog = newsLog;
            _logger = logger;
            _spoolDirectory = SpoolDirectoryUtilities.ResolveSpoolDirectory(options.Value);
        }

        /// <summary>
        /// Runs the worker loop until the queue completes, the worker token is canceled, or the reader closes.
        /// </summary>
        /// <param name="cancellationToken">
        /// Per-worker token linked to host shutdown and pool scale-down. Passed to queue reads, postprocessing, and
        /// atomic writes. History release and commit helpers intentionally use <see cref="CancellationToken.None"/>
        /// instead.
        /// </param>
        /// <returns>
        /// A task that completes normally when the channel reader closes or cancellation is observed on queue read or
        /// during an in-flight atomic write. Does not fault on those expected exit paths. An unexpected exception from
        /// postprocessing (other than write-path cancellation handling) may fault the returned task after
        /// <see cref="NntpSpoolWriteQueue.NotifyDequeued(int)"/> runs in <c>finally</c>.
        /// </returns>
        /// <remarks>
        /// <para><b>Success path (per item):</b></para>
        /// <list type="number">
        /// <item><description>Record article type flags from postprocessing.</description></item>
        /// <item><description>Resolve path, ensure leaf directory, atomic write postprocessed bytes.</description></item>
        /// <item><description>Record write success and acceptance metrics; log news <c>+</c> line.</description></item>
        /// <item><description>Emit cancel-processed news line when <see cref="ArticleTypeFlags.Cancel"/> is set.</description></item>
        /// <item><description>Commit history via <see cref="TryCommitHistoryReservationAsync"/>.</description></item>
        /// </list>
        /// <para><b>Failure path (per item):</b></para>
        /// <list type="number">
        /// <item><description>Record stage-specific failure metric and structured pump log.</description></item>
        /// <item><description>Classify and record rejection metric; emit news <c>-</c> line.</description></item>
        /// <item><description>Release history via <see cref="TryReleaseHistoryReservationAsync"/>.</description></item>
        /// <item><description>Continue the loop without terminating the worker (write exceptions are caught locally).</description></item>
        /// </list>
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
        /// <see cref="NntpSpoolWriterPool"/> starts each worker through <see cref="Task.Run(Func{Task}, CancellationToken)"/>
        /// and observes task completion during scale-down or host stop.
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
                    Stopwatch preprocessStopwatch = Stopwatch.StartNew();
                    using Activity? preprocessActivity = ArticlesSpoolTelemetry.ActivitySource.StartActivity(
                        ArticlesSpoolTelemetry.PreprocessOperation,
                        ActivityKind.Internal);
                    ArticleSpoolPreprocessResult preprocessResult = await _preprocessor
                        .PreprocessAsync(item.MessageId, item.ArticleBytes)
                        .ConfigureAwait(false);
                    preprocessStopwatch.Stop();
                    _metrics.RecordPreprocessDuration(preprocessStopwatch.Elapsed.TotalMilliseconds);
                    if (!preprocessResult.Success)
                    {
                        preprocessActivity?.SetStatus(ActivityStatusCode.Error, "preprocess rejected");
                        _metrics.RecordPreprocessFailure();
                        LogPreprocessFailed(_logger, item.MessageId, preprocessResult.FailureReason);
                        _metrics.RecordArticleRejected(
                            item.Origin,
                            item.ArticleBytes,
                            SpoolArticleRejectionClassifier.ClassifyPreprocessFailure(preprocessResult.FailureReason));
                        _newsLog.LogRejected(
                            item.MessageId,
                            item.Origin,
                            item.ArticleBytes,
                            preprocessResult.FailureReason ?? "Rejected");
                        await TryReleaseHistoryReservationAsync(item.MessageId).ConfigureAwait(false);
                        continue;
                    }

                    Stopwatch postprocessStopwatch = Stopwatch.StartNew();
                    using Activity? postprocessActivity = ArticlesSpoolTelemetry.ActivitySource.StartActivity(
                        ArticlesSpoolTelemetry.PostprocessOperation,
                        ActivityKind.Internal);
                    ArticleSpoolPostprocessResult postprocessResult;
                    try
                    {
                        postprocessResult = await _postprocessor
                            .PostprocessAsync(item, preprocessResult.ArticleBytes, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        postprocessActivity?.SetStatus(ActivityStatusCode.Error, "postprocess canceled");
                        await TryReleaseHistoryReservationAsync(item.MessageId).ConfigureAwait(false);
                        return;
                    }

                    postprocessStopwatch.Stop();
                    _metrics.RecordPostprocessDuration(postprocessStopwatch.Elapsed.TotalMilliseconds);
                    if (!postprocessResult.Success)
                    {
                        postprocessActivity?.SetStatus(ActivityStatusCode.Error, "postprocess rejected");
                        _metrics.RecordPostprocessFailure();
                        LogPostprocessFailed(_logger, item.MessageId, postprocessResult.FailureReason);
                        _metrics.RecordArticleRejected(
                            item.Origin,
                            preprocessResult.ArticleBytes,
                            SpoolArticleRejectionClassifier.ClassifyPostprocessFailure(postprocessResult.FailureReason));
                        _newsLog.LogRejected(
                            item.MessageId,
                            item.Origin,
                            preprocessResult.ArticleBytes,
                            postprocessResult.FailureReason ?? "Rejected");
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
                            EnsureArticleDirectoryExists(articleDirectory);
                        }

                        Stopwatch writeStopwatch = Stopwatch.StartNew();
                        using Activity? writeActivity = ArticlesSpoolTelemetry.ActivitySource.StartActivity(
                            ArticlesSpoolTelemetry.WriteOperation,
                            ActivityKind.Client);
                        await FileIOUtilities.AtomicWriteAsync(articlePath, postprocessResult.ArticleBytes, cancellationToken).ConfigureAwait(false);
                        writeStopwatch.Stop();
                        _metrics.RecordWriteDuration(writeStopwatch.Elapsed.TotalMilliseconds);
                        _metrics.RecordWriteSuccess(postprocessResult.ArticleBytes.Length);
                        _metrics.RecordArticleAccepted(item.Origin, postprocessResult.ArticleBytes);
                        _newsLog.LogAccepted(
                            item.MessageId,
                            item.Origin,
                            postprocessResult.ArticleBytes);
                        if ((postprocessResult.ArticleType & ArticleTypeFlags.Cancel) != 0)
                        {
                            _newsLog.LogCancelProcessed(
                                item.MessageId,
                                item.Origin,
                                postprocessResult.ArticleBytes);
                        }

                        await TryCommitHistoryReservationAsync(item.MessageId).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        await TryReleaseHistoryReservationAsync(item.MessageId).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _metrics.RecordWriteFailure();
                        LogWriteFailed(_logger, ex, item.MessageId, item.MessageIdDigestHex);
                        _metrics.RecordArticleRejected(
                            item.Origin,
                            postprocessResult.ArticleBytes,
                            SpoolArticleRejectionClassifier.ClassifyWriteFailure());
                        _newsLog.LogRejected(
                            item.MessageId,
                            item.Origin,
                            postprocessResult.ArticleBytes,
                            ex.GetType().Name);
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
        /// Must be non-empty; callers pass the parent of the digest file path from
        /// <see cref="SpoolDirectoryUtilities.GetArticleFilePath"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="RunAsync"/> immediately before
        /// <see cref="FileIOUtilities.AtomicWriteAsync"/>. Concurrent workers may race on
        /// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/>; losers skip creation because the winner already
        /// created the directory. <see cref="Directory.CreateDirectory(string)"/> is idempotent when the directory
        /// already exists.
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
                    _ = Directory.CreateDirectory(articleDirectory);
                }
                catch
                {
                    _ = _knownDirectories.TryRemove(articleDirectory, out _);
                    throw;
                }
            }
        }

        /// <summary>
        /// Releases a HistoryDB reservation after spool preprocessing, postprocessing, write failure, or write cancellation.
        /// </summary>
        /// <param name="messageId">NNTP <c>Message-ID</c> whose digest reservation should be removed.</param>
        /// <returns>
        /// A task that completes after the release attempt finishes. Never throws; failures are logged through the
        /// logging partial and counted in metrics.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Uses <see cref="CancellationToken.None"/> so cleanup completes even when the worker token is already canceled.
        /// Invoked from <see cref="RunAsync"/> failure paths and from the atomic-write cancellation handler. Successful
        /// writes intentionally retain the reservation and call <see cref="TryCommitHistoryReservationAsync"/> instead.
        /// </para>
        /// <para>
        /// <see cref="HistoryReleaseResult.Released"/> and <see cref="HistoryReleaseResult.NotFound"/> are treated as
        /// success. <see cref="HistoryReleaseResult.TryAgainLater"/> and
        /// <see cref="HistoryReleaseResult.Unavailable"/> increment failure metrics and emit warning logs via
        /// <c>LogHistoryReleaseOutcome</c>. Exceptions increment failure metrics and emit error logs via
        /// <c>LogHistoryReleaseFailed</c>.
        /// </para>
        /// </remarks>
        private async Task TryReleaseHistoryReservationAsync(string messageId)
        {
            using Activity? activity = ArticlesSpoolTelemetry.ActivitySource.StartActivity(
                ArticlesSpoolTelemetry.HistoryReleaseOperation,
                ActivityKind.Internal);
            try
            {
                HistoryReleaseResult releaseResult = await _historyDatabase
                    .TryReleaseAsync(messageId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (releaseResult is not (HistoryReleaseResult.Released or HistoryReleaseResult.NotFound))
                {
                    activity?.SetStatus(ActivityStatusCode.Error, releaseResult.ToString());
                    _metrics.RecordHistoryReleaseFailure();
                    LogHistoryReleaseOutcome(_logger, releaseResult, messageId);
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                _metrics.RecordHistoryReleaseFailure();
                LogHistoryReleaseFailed(_logger, ex, messageId, ex.GetType().Name);
            }
        }

        /// <summary>
        /// Commits a HistoryDB digest reservation after successful spool persistence.
        /// </summary>
        /// <param name="messageId">NNTP <c>Message-ID</c> whose digest should remain in all history tiers.</param>
        /// <returns>
        /// A task that completes after the commit attempt finishes. Never throws; failures are logged through the
        /// logging partial and counted in metrics.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Uses <see cref="CancellationToken.None"/> so commit completes even when the worker token is already canceled.
        /// Invoked only from <see cref="RunAsync"/> after <see cref="FileIOUtilities.AtomicWriteAsync"/> succeeds. When
        /// TAKETHIS/IHAVE already reserved the digest, <see cref="HistoryRecordResult.Duplicate"/> is treated as success.
        /// </para>
        /// <para>
        /// <see cref="HistoryRecordResult.Recorded"/> and <see cref="HistoryRecordResult.Duplicate"/> are success.
        /// <see cref="HistoryRecordResult.TryAgainLater"/> and <see cref="HistoryRecordResult.Unavailable"/> increment
        /// failure metrics and emit warning logs via <c>LogHistoryCommitOutcome</c>. Exceptions increment failure metrics
        /// and emit error logs via <c>LogHistoryCommitFailed</c>. The spool file is always retained on commit failure.
        /// </para>
        /// </remarks>
        private async Task TryCommitHistoryReservationAsync(string messageId)
        {
            using Activity? activity = ArticlesSpoolTelemetry.ActivitySource.StartActivity(
                ArticlesSpoolTelemetry.HistoryCommitOperation,
                ActivityKind.Internal);
            try
            {
                HistoryRecordResult recordResult = await _historyDatabase
                    .TryRecordAsync(messageId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (recordResult is not (HistoryRecordResult.Recorded or HistoryRecordResult.Duplicate))
                {
                    activity?.SetStatus(ActivityStatusCode.Error, recordResult.ToString());
                    _metrics.RecordHistoryCommitFailure();
                    LogHistoryCommitOutcome(_logger, recordResult, messageId);
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                _metrics.RecordHistoryCommitFailure();
                LogHistoryCommitFailed(_logger, ex, messageId, ex.GetType().Name);
            }
        }
    }
}
