// <copyright file="NntpSpoolWriterPump.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 1-9 (spool writer pump worker failures). Scaling diagnostics use 700-719 in NntpSpoolWriterPool.Logging.cs.

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> logging partial for
    /// <see cref="NntpSpoolWriterPump"/> per-article failure and history cleanup diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Keeps hot-path dequeue and I/O code in <c>NntpSpoolWriterPump.cs</c> while centralizing EventId
    /// assignments, log levels, and message templates here. Each helper is a <c>private static partial</c> method
    /// expanded at compile time by the logging source generator; callers pass the pump instance's
    /// <c>ILogger&lt;NntpSpoolWriterPump&gt;</c> as <see cref="ILogger"/>.
    /// </para>
    /// <para>
    /// These methods emit only failure and history-repair paths. Successful spool writes, accepted articles, and
    /// steady-state dequeue activity are not logged from this partial (acceptance is recorded via
    /// <see cref="Logging.INntpNewsLog"/> and <see cref="Metrics.NntpSpoolMetrics"/> instead).
    /// </para>
    /// <para>
    /// <b>Invocation context:</b> All helpers are called from <see cref="RunAsync"/> or its private
    /// <see cref="TryReleaseHistoryReservationAsync"/> /
    /// <see cref="TryCommitHistoryReservationAsync"/> helpers. Multiple concurrent
    /// <see cref="RunAsync"/> workers may log interleaved lines; each entry includes
    /// <c>MessageId</c> (and digest or exception fields where applicable) for correlation.
    /// </para>
    /// <para>
    /// <b>EventId bands (Articles spool):</b> worker failures 1-9 (this partial), queue management 10-19 (reserved),
    /// scaling 700-719 (<see cref="NntpSpoolWriterPool"/> logging partial), shutdown 720-729 (reserved). Assign new pump
    /// diagnostics within 1-9 before extending the band.
    /// </para>
    /// <para><b>EventIds defined in this partial:</b></para>
    /// <list type="table">
    /// <listheader><term>EventId</term><description>Pipeline phase and level</description></listheader>
    /// <item><term>1</term><description>Preprocess rejection — <see cref="LogLevel.Warning"/>.</description></item>
    /// <item><term>2</term><description>Atomic spool write exception — <see cref="LogLevel.Error"/>.</description></item>
    /// <item><term>3</term><description>History release non-success outcome after spool failure — <see cref="LogLevel.Warning"/>.</description></item>
    /// <item><term>4</term><description>History release exception after spool failure — <see cref="LogLevel.Error"/>.</description></item>
    /// <item><term>5</term><description>Postprocess rejection — <see cref="LogLevel.Warning"/>.</description></item>
    /// <item><term>6</term><description>History commit non-success outcome after successful spool write — <see cref="LogLevel.Warning"/>.</description></item>
    /// <item><term>7</term><description>History commit exception after successful spool write — <see cref="LogLevel.Error"/>.</description></item>
    /// <item><term>8-9</term><description>Reserved; unassigned in this repository revision.</description></item>
    /// </list>
    /// <para><b>Threading:</b> Static helpers have no mutable state and are safe to call from any writer worker thread
    /// without external synchronization.</para>
    /// </remarks>
    internal sealed partial class NntpSpoolWriterPump
    {
        /// <summary>
        /// Logs header syntax validation or <c>PathAppend</c> path-hop mutation failure for a dequeued article.
        /// </summary>
        /// <param name="logger">
        /// Pump category logger (typically the <see cref="NntpSpoolWriterPump"/> instance field passed from
        /// <see cref="RunAsync"/>).
        /// </param>
        /// <param name="messageId">NNTP <c>Message-ID</c> of the article that failed preprocessing.</param>
        /// <param name="failureReason">
        /// Human-readable reason from <see cref="Processing.ArticleSpoolPreprocessor"/> (for example invalid header
        /// syntax or path-hop mutation error). May be <see langword="null"/> when the preprocessor did not supply text;
        /// the log template still emits the field for structured-query consistency.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="RunAsync"/> when <see cref="Processing.ArticleSpoolPreprocessor.PreprocessAsync"/>
        /// returns <see cref="Processing.ArticleSpoolPreprocessResult.Success"/> <see langword="false"/>. Emitted at
        /// <see cref="LogLevel.Warning"/> immediately after <see cref="Metrics.NntpSpoolMetrics.RecordPreprocessFailure"/>,
        /// before rejection metrics, <see cref="Logging.INntpNewsLog.LogRejected"/>, and
        /// <see cref="TryReleaseHistoryReservationAsync"/>.
        /// </para>
        /// <para>The article payload is not written to spool. The worker continues draining the queue.</para>
        /// </remarks>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Spool preprocess failed for message-id {MessageId}: {FailureReason}")]
        private static partial void LogPreprocessFailed(ILogger logger, string messageId, string? failureReason);

        /// <summary>
        /// Logs deep header semantics, Message-ID, date, filter, yEnc CRC, or spamd rejection for a preprocessed article.
        /// </summary>
        /// <param name="logger">
        /// Pump category logger (typically the <see cref="NntpSpoolWriterPump"/> instance field passed from
        /// <see cref="RunAsync"/>).
        /// </param>
        /// <param name="messageId">NNTP <c>Message-ID</c> of the article that failed postprocessing.</param>
        /// <param name="failureReason">
        /// Human-readable reason from <see cref="Processing.ArticleSpoolPostprocessor"/> (for example invalid
        /// <c>Message-ID</c> header, unparsable <c>Date</c>, forbidden header, yEnc CRC failure, or spamd rejection).
        /// May be
        /// <see langword="null"/> when the postprocessor did not supply text.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="RunAsync"/> when <see cref="Processing.ArticleSpoolPostprocessor.PostprocessAsync"/>
        /// returns <see cref="Processing.ArticleSpoolPostprocessResult.Success"/> <see langword="false"/>. Emitted at
        /// <see cref="LogLevel.Warning"/> immediately after <see cref="Metrics.NntpSpoolMetrics.RecordPostprocessFailure"/>,
        /// before rejection metrics, <see cref="Logging.INntpNewsLog.LogRejected"/>, and
        /// <see cref="TryReleaseHistoryReservationAsync"/>.
        /// </para>
        /// <para>
        /// Preprocessing has already succeeded. The article payload is not written to spool. The worker continues
        /// draining the queue.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 5,
            Level = LogLevel.Warning,
            Message = "Spool postprocess failed for message-id {MessageId}: {FailureReason}")]
        private static partial void LogPostprocessFailed(ILogger logger, string messageId, string? failureReason);

        /// <summary>
        /// Logs an unexpected exception during digest directory preparation or atomic spool payload write.
        /// </summary>
        /// <param name="logger">
        /// Pump category logger (typically the <see cref="NntpSpoolWriterPump"/> instance field passed from
        /// <see cref="RunAsync"/>).
        /// </param>
        /// <param name="ex">
        /// Exception thrown by <see cref="EnsureArticleDirectoryExists"/>,
        /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/>, or related filesystem operations. Attached to
        /// the log entry for stack-trace diagnosis even though the message template names only Message-ID and digest.
        /// </param>
        /// <param name="messageId">NNTP <c>Message-ID</c> of the article whose write failed.</param>
        /// <param name="messageIdDigestHex">
        /// Lowercase Blake3 digest hex from the queued <see cref="NntpSpoolWriteItem"/> used by
        /// <see cref="Diagnostics.SpoolDirectoryUtilities.GetArticleFilePath"/> so operators can locate the intended path
        /// under <c>Incoming/{aa}/{bb}/</c>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from the <see cref="RunAsync"/> <c>catch (Exception)</c> block around atomic write — not from
        /// <see cref="OperationCanceledException"/> paths (worker cancellation releases history and exits without this
        /// log). Emitted at <see cref="LogLevel.Error"/> immediately after
        /// <see cref="Metrics.NntpSpoolMetrics.RecordWriteFailure"/>, before rejection metrics,
        /// <see cref="Logging.INntpNewsLog.LogRejected"/>, and <see cref="TryReleaseHistoryReservationAsync"/>.
        /// </para>
        /// <para>Postprocessing has already succeeded. The worker continues draining the queue.</para>
        /// </remarks>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Error,
            Message = "Spool write failed for message-id {MessageId} (digest {MessageIdDigestHex}).")]
        private static partial void LogWriteFailed(
            ILogger logger,
            Exception ex,
            string messageId,
            string messageIdDigestHex);

        /// <summary>
        /// Logs a non-success <see cref="HistoryDB.Abstractions.HistoryReleaseResult"/> after spool failure cleanup.
        /// </summary>
        /// <param name="logger">
        /// Pump category logger passed from <see cref="TryReleaseHistoryReservationAsync"/>.
        /// </param>
        /// <param name="releaseResult">
        /// Outcome from <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/>. This method is called only
        /// for <see cref="HistoryDB.Abstractions.HistoryReleaseResult.TryAgainLater"/> and
        /// <see cref="HistoryDB.Abstractions.HistoryReleaseResult.Unavailable"/>; <see cref="HistoryDB.Abstractions.HistoryReleaseResult.Released"/>
        /// and <see cref="HistoryDB.Abstractions.HistoryReleaseResult.NotFound"/> are treated as success and not logged here.
        /// </param>
        /// <param name="messageId">NNTP <c>Message-ID</c> whose history reservation could not be released cleanly.</param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="TryReleaseHistoryReservationAsync"/> after preprocess, postprocess, write failure, or
        /// worker cancellation during an in-flight write. Emitted at <see cref="LogLevel.Warning"/> immediately after
        /// <see cref="Metrics.NntpSpoolMetrics.RecordHistoryReleaseFailure"/>.
        /// </para>
        /// <para>
        /// A stuck reservation may cause peers to receive duplicate <c>CHECK</c> rejections until history state is
        /// repaired. Exceptions from <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/> are logged by
        /// <see cref="LogHistoryReleaseFailed"/> instead.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "History reservation release returned {ReleaseResult} for message-id {MessageId} after spool failure.")]
        private static partial void LogHistoryReleaseOutcome(
            ILogger logger,
            HistoryDB.Abstractions.HistoryReleaseResult releaseResult,
            string messageId);

        /// <summary>
        /// Logs an exception thrown while releasing a HistoryDB reservation after spool failure.
        /// </summary>
        /// <param name="logger">
        /// Pump category logger passed from <see cref="TryReleaseHistoryReservationAsync"/>.
        /// </param>
        /// <param name="ex">Exception from <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/>.</param>
        /// <param name="messageId">NNTP <c>Message-ID</c> whose history reservation release faulted.</param>
        /// <param name="exceptionType">
        /// Short exception type name from <see cref="Exception.GetType"/>, duplicated as a structured field for
        /// dashboard grouping alongside the attached <paramref name="ex"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from the <see cref="TryReleaseHistoryReservationAsync"/> <c>catch (Exception)</c> block. Emitted at
        /// <see cref="LogLevel.Error"/> immediately after <see cref="Metrics.NntpSpoolMetrics.RecordHistoryReleaseFailure"/>.
        /// The exception is swallowed after logging so the worker loop continues.
        /// </para>
        /// <para>
        /// Indicates the message-id may remain reserved in HistoryDB until an operator or retry path clears it.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Error,
            Message = "History reservation release failed for message-id {MessageId} after spool failure ({ExceptionType}).")]
        private static partial void LogHistoryReleaseFailed(
            ILogger logger,
            Exception ex,
            string messageId,
            string exceptionType);

        /// <summary>
        /// Logs a non-success <see cref="HistoryDB.Abstractions.HistoryRecordResult"/> after a successful spool write.
        /// </summary>
        /// <param name="logger">
        /// Pump category logger passed from <see cref="TryCommitHistoryReservationAsync"/>.
        /// </param>
        /// <param name="recordResult">
        /// Outcome from <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryRecordAsync"/>. This method is called only
        /// for <see cref="HistoryDB.Abstractions.HistoryRecordResult.TryAgainLater"/> and
        /// <see cref="HistoryDB.Abstractions.HistoryRecordResult.Unavailable"/>; <see cref="HistoryDB.Abstractions.HistoryRecordResult.Recorded"/>
        /// and <see cref="HistoryDB.Abstractions.HistoryRecordResult.Duplicate"/> are treated as success and not logged here.
        /// </param>
        /// <param name="messageId">NNTP <c>Message-ID</c> whose history digest could not be committed after spool write.</param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="TryCommitHistoryReservationAsync"/> after
        /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/> succeeds. Emitted at
        /// <see cref="LogLevel.Warning"/> immediately after <see cref="Metrics.NntpSpoolMetrics.RecordHistoryCommitFailure"/>.
        /// </para>
        /// <para>
        /// The spool file remains on disk. Duplicate <c>CHECK</c> may incorrectly return <c>wanted</c> until history is
        /// repaired. Exceptions from <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryRecordAsync"/> are logged by
        /// <see cref="LogHistoryCommitFailed"/> instead.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 6,
            Level = LogLevel.Warning,
            Message = "History commit returned {RecordResult} for message-id {MessageId} after spool write.")]
        private static partial void LogHistoryCommitOutcome(
            ILogger logger,
            HistoryDB.Abstractions.HistoryRecordResult recordResult,
            string messageId);

        /// <summary>
        /// Logs an exception thrown while committing history after a successful spool write.
        /// </summary>
        /// <param name="logger">
        /// Pump category logger passed from <see cref="TryCommitHistoryReservationAsync"/>.
        /// </param>
        /// <param name="ex">Exception from <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryRecordAsync"/>.</param>
        /// <param name="messageId">NNTP <c>Message-ID</c> whose history commit faulted.</param>
        /// <param name="exceptionType">
        /// Short exception type name from <see cref="Exception.GetType"/>, duplicated as a structured field for
        /// dashboard grouping alongside the attached <paramref name="ex"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from the <see cref="TryCommitHistoryReservationAsync"/> <c>catch (Exception)</c> block. Emitted at
        /// <see cref="LogLevel.Error"/> immediately after <see cref="Metrics.NntpSpoolMetrics.RecordHistoryCommitFailure"/>.
        /// The exception is swallowed after logging so the worker loop continues.
        /// </para>
        /// <para>
        /// The spool file remains on disk. Duplicate <c>CHECK</c> may incorrectly return <c>wanted</c> until history is
        /// repaired.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 7,
            Level = LogLevel.Error,
            Message = "History commit failed for message-id {MessageId} after spool write ({ExceptionType}).")]
        private static partial void LogHistoryCommitFailed(
            ILogger logger,
            Exception ex,
            string messageId,
            string exceptionType);
    }
}
