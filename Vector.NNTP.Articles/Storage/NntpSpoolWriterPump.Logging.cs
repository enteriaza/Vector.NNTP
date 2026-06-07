// <copyright file="NntpSpoolWriterPump.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventId range: 1-9 (spool writer pump worker failures). Scaling diagnostics use 700-719 in NntpSpoolWriterPool.Logging.cs.

namespace Vector.NNTP.Articles.Storage
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for <see cref="NntpSpoolWriterPump"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These methods are invoked from the writer worker loop after preprocess, postprocess, or atomic write failures, and when
    /// <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/> returns an outcome that requires operator attention. Successful
    /// spool writes do not emit log lines from this partial.
    /// </para>
    /// <para>
    /// <b>EventId bands (Articles spool):</b> worker failures 1-9, queue management 10-19, scaling 700-719, shutdown
    /// 720-729. New pump diagnostics should stay within 1-9 unless the band is extended deliberately.
    /// </para>
    /// <para><b>EventIds in this partial:</b></para>
    /// <list type="table">
    /// <listheader><term>EventId</term><description>Meaning</description></listheader>
    /// <item><term>1</term><description>Preprocess failure (<see cref="LogLevel.Warning"/>).</description></item>
    /// <item><term>2</term><description>Atomic write failure (<see cref="LogLevel.Error"/>).</description></item>
    /// <item><term>3</term><description>History reservation release non-success outcome (<see cref="LogLevel.Warning"/>).</description></item>
    /// <item><term>4</term><description>History reservation release exception (<see cref="LogLevel.Error"/>).</description></item>
    /// <item><term>5</term><description>Postprocess failure (<see cref="LogLevel.Warning"/>).</description></item>
    /// </list>
    /// </remarks>
    internal sealed partial class NntpSpoolWriterPump
    {
        /// <summary>
        /// Logs header validation or path-mutation failure for a dequeued article.
        /// </summary>
        /// <param name="logger">Writer pump category logger.</param>
        /// <param name="messageId">NNTP Message-ID of the article that failed preprocessing.</param>
        /// <param name="failureReason">
        /// Human-readable reason from <see cref="Processing.ArticleSpoolPreprocessor"/> (for example invalid header
        /// syntax or path-hop mutation error). May be <see langword="null"/> when the preprocessor did not supply text.
        /// </param>
        /// <remarks>
        /// Emitted at <see cref="LogLevel.Warning"/> after <see cref="Metrics.NntpSpoolMetrics.RecordPreprocessFailure"/>
        /// and before history release is attempted. The article payload is not written to spool.
        /// </remarks>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Spool preprocess failed for message-id {MessageId}: {FailureReason}")]
        private static partial void LogPreprocessFailed(ILogger logger, string messageId, string? failureReason);

        /// <summary>
        /// Logs deep header validation or filter rejection for a dequeued article.
        /// </summary>
        /// <param name="logger">Writer pump category logger.</param>
        /// <param name="messageId">NNTP Message-ID of the article that failed postprocessing.</param>
        /// <param name="failureReason">
        /// Human-readable reason from <see cref="Processing.ArticleSpoolPostprocessor"/> (for example invalid
        /// <c>Message-ID</c> header, unparsable <c>Date</c>, or forbidden header). May be <see langword="null"/> when the
        /// postprocessor did not supply text.
        /// </param>
        /// <remarks>
        /// Emitted at <see cref="LogLevel.Warning"/> after <see cref="Metrics.NntpSpoolMetrics.RecordPostprocessFailure"/>
        /// and before history release is attempted. The article payload is not written to spool.
        /// </remarks>
        [LoggerMessage(
            EventId = 5,
            Level = LogLevel.Warning,
            Message = "Spool postprocess failed for message-id {MessageId}: {FailureReason}")]
        private static partial void LogPostprocessFailed(ILogger logger, string messageId, string? failureReason);

        /// <summary>
        /// Logs an unexpected exception during atomic spool payload write.
        /// </summary>
        /// <param name="logger">Writer pump category logger.</param>
        /// <param name="ex">Exception thrown by <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/> or directory preparation.</param>
        /// <param name="messageId">NNTP Message-ID of the article whose write failed.</param>
        /// <param name="messageIdDigestHex">
        /// Lowercase Blake3 digest hex used by <see cref="Diagnostics.SpoolDirectoryUtilities.GetArticleFilePath"/> so
        /// operators can locate the intended path under <c>Incoming/{aa}/{bb}/</c>.
        /// </param>
        /// <remarks>
        /// Emitted at <see cref="LogLevel.Error"/> after <see cref="Metrics.NntpSpoolMetrics.RecordWriteFailure"/>.
        /// Postprocessing has already succeeded; the exception is attached to the log entry for stack-trace diagnosis.
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
        /// <param name="logger">Writer pump category logger.</param>
        /// <param name="releaseResult">
        /// Outcome from <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/>. This method is called only
        /// for <see cref="HistoryDB.Abstractions.HistoryReleaseResult.TryAgainLater"/> and
        /// <see cref="HistoryDB.Abstractions.HistoryReleaseResult.Unavailable"/>; <c>Released</c> and <c>NotFound</c> are
        /// treated as success and not logged here.
        /// </param>
        /// <param name="messageId">NNTP Message-ID whose history reservation could not be released cleanly.</param>
        /// <remarks>
        /// Emitted at <see cref="LogLevel.Warning"/> after <see cref="Metrics.NntpSpoolMetrics.RecordHistoryReleaseFailure"/>.
        /// A stuck reservation may cause peers to receive duplicate CHECK rejections until history state is repaired.
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
        /// Logs an exception thrown while releasing history after spool preprocess, postprocess, or write failure.
        /// </summary>
        /// <param name="logger">Writer pump category logger.</param>
        /// <param name="ex">Exception from <see cref="HistoryDB.Abstractions.IHistoryDatabase.TryReleaseAsync"/>.</param>
        /// <param name="messageId">NNTP Message-ID whose history reservation release faulted.</param>
        /// <param name="exceptionType">
        /// Short exception type name from <see cref="Exception.GetType"/>, duplicated as a structured field for
        /// dashboard grouping alongside the attached exception.
        /// </param>
        /// <remarks>
        /// Emitted at <see cref="LogLevel.Error"/> after <see cref="Metrics.NntpSpoolMetrics.RecordHistoryReleaseFailure"/>.
        /// The worker continues draining the queue; this log indicates the message-id may remain reserved in HistoryDB.
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
    }
}
