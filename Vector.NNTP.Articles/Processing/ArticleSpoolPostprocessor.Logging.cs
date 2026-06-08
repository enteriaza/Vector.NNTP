// <copyright file="ArticleSpoolPostprocessor.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH (Tier 2 logging): LoggerMessage definitions for spamd fail-open events on eligible articles.
// EventId range: 100-199 (ArticleSpoolPostprocessor spamd fail-open).

namespace Vector.NNTP.Articles.Processing
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> logging partial for
    /// <see cref="ArticleSpoolPostprocessor"/> spamd fail-open diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Keeps validation and spam-check control flow in <c>ArticleSpoolPostprocessor.cs</c> while
    /// centralizing EventId assignments, log levels, and message templates here. Each helper is a
    /// <c>private static partial</c> method expanded at compile time by the logging source generator; callers pass the
    /// postprocessor instance's <c>ILogger&lt;ArticleSpoolPostprocessor&gt;</c> as <see cref="ILogger"/>.
    /// </para>
    /// <para>
    /// <b>Emission scope:</b> These methods run only from <c>TrySpamCheckAsync</c> when SpamAssassin
    /// <c>CHECK</c> faults after eligibility checks pass (article is not yEnc and is under the 131072-byte spam gate).
    /// Header validation, filter rejections, yEnc CRC failures, and spam <em>classification</em> rejections are not logged
    /// here — semantic rejections return <see cref="ArticleSpoolPostprocessResult"/> to
    /// <see cref="Storage.NntpSpoolWriterPump"/>, which logs via <c>LogPostprocessFailed</c> on its own logger category.
    /// </para>
    /// <para>
    /// <b>Fail-open policy:</b> Protocol and unexpected spamd faults accept the article (<c>TrySpamCheckAsync</c>
    /// returns <see langword="null"/> and postprocessing continues). Warnings are logged here so operators can correlate
    /// transient spamd outages without dropping transit payloads. Worker cancellation during an in-flight spamd call
    /// rethrows <see cref="OperationCanceledException"/> and is not logged from this partial.
    /// </para>
    /// <para>
    /// <b>EventId band:</b> EventIds 100-199 are scoped to <c>ILogger&lt;ArticleSpoolPostprocessor&gt;</c>. Pump worker
    /// failures use EventIds 1-99 on <c>ILogger&lt;NntpSpoolWriterPump&gt;</c>.
    /// </para>
    /// <para><b>EventIds defined in this partial:</b></para>
    /// <list type="table">
    /// <listheader><term>EventId</term><description>Failure class and level</description></listheader>
    /// <item><term>100</term><description><see cref="Filters.SpamAssassin.SpamdProtocolException"/> — <see cref="LogLevel.Warning"/>.</description></item>
    /// <item><term>101</term><description>Unexpected exception (including scan-build faults and non-protocol spamd errors) — <see cref="LogLevel.Warning"/>.</description></item>
    /// </list>
    /// <para><b>Threading:</b> Static helpers have no mutable state and are safe to call from any writer worker thread
    /// without external synchronization.</para>
    /// </remarks>
    internal sealed partial class ArticleSpoolPostprocessor
    {
        /// <summary>
        /// Logs a spamd protocol or wire-session failure that fails open and accepts the article.
        /// </summary>
        /// <param name="logger">
        /// Postprocessor category logger (the <see cref="ArticleSpoolPostprocessor"/> instance field passed from
        /// <c>TrySpamCheckAsync</c>).
        /// </param>
        /// <param name="exception">
        /// <see cref="Filters.SpamAssassin.SpamdProtocolException"/> from <see cref="Filters.SpamAssassin.ISpamAssassin.CheckAsync"/>.
        /// Captured on the log event for structured exception fields (for example
        /// <see cref="Filters.SpamAssassin.SpamdProtocolException.ExitCode"/> when populated).
        /// </param>
        /// <param name="messageId">
        /// Transit <c>Message-ID</c> from the dequeued <see cref="Storage.NntpSpoolWriteItem"/> under spam check.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <c>TrySpamCheckAsync</c> when <see cref="Filters.SpamAssassin.ISpamAssassin.CheckAsync"/> throws
        /// <see cref="Filters.SpamAssassin.SpamdProtocolException"/>. Emitted at <see cref="LogLevel.Warning"/> before
        /// returning <see langword="null"/> so <see cref="PostprocessAsync"/> can complete with a successful result.
        /// </para>
        /// <para>
        /// Does not record <see cref="Metrics.NntpSpoolMetrics.RecordArticleRejected"/>; the article is accepted on the
        /// fail-open path. Pair with <see cref="Metrics.NntpSpoolMetrics.RecordSpamdFailOpen"/>.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 100,
            Level = LogLevel.Warning,
            Message = "Spamd check failed open for Message-ID {MessageId}; accepting article.")]
        private static partial void LogSpamdFailedOpen(ILogger logger, Exception exception, string messageId);

        /// <summary>
        /// Logs an unexpected spamd or scan-build failure that fails open and accepts the article.
        /// </summary>
        /// <param name="logger">
        /// Postprocessor category logger (the <see cref="ArticleSpoolPostprocessor"/> instance field passed from
        /// <c>TrySpamCheckAsync</c>).
        /// </param>
        /// <param name="exception">
        /// Any <see cref="Exception"/> other than <see cref="Filters.SpamAssassin.SpamdProtocolException"/> and
        /// cancellation. Includes scan synthesis faults from <see cref="SpamdScanArticleBuilder.BuildScanArticle"/>,
        /// transport errors not wrapped as protocol exceptions, and other spamd client faults.
        /// </param>
        /// <param name="messageId">
        /// Transit <c>Message-ID</c> from the dequeued <see cref="Storage.NntpSpoolWriteItem"/> under spam check.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <c>TrySpamCheckAsync</c> on the general <c>catch (Exception)</c> path after
        /// <see cref="Filters.SpamAssassin.SpamdProtocolException"/> handling and outside worker cancellation. Emitted at
        /// <see cref="LogLevel.Warning"/> before returning <see langword="null"/> so postprocessing can succeed.
        /// </para>
        /// <para>
        /// Does not record <see cref="Metrics.NntpSpoolMetrics.RecordArticleRejected"/>; the article is accepted on the
        /// fail-open path. Pair with <see cref="Metrics.NntpSpoolMetrics.RecordSpamdFailOpen"/>.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 101,
            Level = LogLevel.Warning,
            Message = "Unexpected spamd check failure for Message-ID {MessageId}; accepting article (fail-open).")]
        private static partial void LogSpamdUnexpectedFailure(ILogger logger, Exception exception, string messageId);
    }
}
