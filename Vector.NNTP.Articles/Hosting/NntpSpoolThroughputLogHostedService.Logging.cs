// <copyright file="NntpSpoolThroughputLogHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 1-2 (spool throughput minute summaries to the main host application log).

using Vector.NNTP.Articles.Metrics;

namespace Vector.NNTP.Articles.Hosting
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers that format spool throughput minute summaries for the host log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Logging partial for <see cref="NntpSpoolThroughputLogHostedService"/>. Formats deltas from
    /// <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/> into single-line
    /// <see cref="LogLevel.Information"/> records on the main host <see cref="ILogger"/> pipeline (for example
    /// <c>NNTPD-.log</c> and console). Never writes to the dedicated INN <c>news-{date}.log</c> sink used by
    /// <see cref="Logging.INntpNewsLog"/>.
    /// </para>
    /// <para><b>Event identifiers:</b></para>
    /// <list type="bullet">
    /// <item><description>EventId <c>1</c> — global rollup (<see cref="LogGlobalThroughput"/>).</description></item>
    /// <item><description>EventId <c>2</c> — per-feed row (<see cref="LogFeedThroughput"/>).</description></item>
    /// </list>
    /// <para>
    /// Snapshot capture failures are logged at Error EventId <c>3</c> by
    /// <c>NntpSpoolThroughputLogHostedService.LogSnapshotFailed</c> on the hosted-service partial, not in this file.
    /// </para>
    /// <para>
    /// <b>Rejection buckets:</b> <c>header</c>, <c>crc</c>, <c>crosspost</c>, and <c>other</c> in log templates map to
    /// <see cref="SpoolArticleRejectionCategory"/> values recorded through
    /// <see cref="NntpSpoolMetrics.RecordArticleRejected"/> and aligned with INN minus-line reasons, not to OpenTelemetry
    /// tag strings from <see cref="SpoolArticleRejectionMetricsTags"/>.
    /// </para>
    /// <para><b>Threading:</b> Static helpers; safe to call from the throughput hosted-service execute task.</para>
    /// </remarks>
    internal static partial class NntpSpoolThroughputLog
    {
        /// <summary>
        /// Emits global and per-feed throughput lines for a minute snapshot when the global rollup shows activity.
        /// </summary>
        /// <param name="logger">
        /// Host category logger (typically <see cref="ILogger{NntpSpoolThroughputLogHostedService}"/>). Must not be
        /// <see cref="Logging.INntpNewsLog"/> or the INN news Serilog sink.
        /// </param>
        /// <param name="snapshot">
        /// Immutable minute delta from <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/>. Per-feed rows in
        /// <see cref="SpoolThroughputMinuteSnapshot.Feeds"/> already exclude feeds with zero
        /// <see cref="SpoolThroughputFeedCounts.Processed"/> in the window.
        /// </param>
        /// <remarks>
        /// <para><b>Emission rules:</b></para>
        /// <list type="number">
        /// <item>
        /// <description>
        /// When <see cref="SpoolThroughputFeedCounts.Processed"/> on <see cref="SpoolThroughputMinuteSnapshot.Global"/> is
        /// zero or negative, returns immediately without logging (idle minute suppression).
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Otherwise emits one global line via <see cref="LogGlobalThroughput"/> (EventId <c>1</c>) using aggregated counts
        /// from the synthetic global row (feed label <see cref="SpoolThroughputMinuteSnapshot.GlobalFeedLabel"/> is not
        /// included in the global message template).
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Then emits one per-feed line via <see cref="LogFeedThroughput"/> (EventId <c>2</c>) for every entry in
        /// <see cref="SpoolThroughputMinuteSnapshot.Feeds"/> in snapshot order (alphabetical by feed name as produced by
        /// metrics).
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Invoked from <see cref="NntpSpoolThroughputLogHostedService"/> once per minute inside a try/catch; unexpected
        /// exceptions are not swallowed here. Never throws for normal snapshot contents.
        /// </para>
        /// </remarks>
        internal static void EmitSnapshot(ILogger logger, SpoolThroughputMinuteSnapshot snapshot)
        {
            if (snapshot.Global.Processed <= 0)
            {
                return;
            }

            LogGlobalThroughput(
                logger,
                snapshot.Global.Processed,
                snapshot.Global.Accepted,
                snapshot.Global.Rejected,
                snapshot.Global.HeaderSyntax,
                snapshot.Global.Crc,
                snapshot.Global.Crosspost,
                snapshot.Global.Other);

            foreach (SpoolThroughputFeedCounts feed in snapshot.Feeds)
            {
                LogFeedThroughput(
                    logger,
                    feed.Feed,
                    feed.Processed,
                    feed.Accepted,
                    feed.Rejected,
                    feed.HeaderSyntax,
                    feed.Crc,
                    feed.Crosspost,
                    feed.Other);
            }
        }

        /// <summary>
        /// Logs the aggregated spool throughput rollup for the last minute (all feeds combined).
        /// </summary>
        /// <param name="logger">Host category logger receiving the formatted line.</param>
        /// <param name="processed">
        /// Total processed articles in the window (<see cref="SpoolThroughputFeedCounts.Processed"/> on the global rollup
        /// row). Rendered with a <c>/min</c> suffix in the message template.
        /// </param>
        /// <param name="accepted">
        /// Accepted articles in the window (<see cref="SpoolThroughputFeedCounts.Accepted"/>).
        /// </param>
        /// <param name="rejected">
        /// Total rejected articles in the window (<see cref="SpoolThroughputFeedCounts.Rejected"/>).
        /// </param>
        /// <param name="header">
        /// Header-syntax rejections (<see cref="SpoolThroughputFeedCounts.HeaderSyntax"/> /
        /// <see cref="SpoolArticleRejectionCategory.HeaderSyntax"/>).
        /// </param>
        /// <param name="crc">
        /// yEnc CRC rejections (<see cref="SpoolThroughputFeedCounts.Crc"/> /
        /// <see cref="SpoolArticleRejectionCategory.Crc"/>).
        /// </param>
        /// <param name="crosspost">
        /// Crosspost limit rejections (<see cref="SpoolThroughputFeedCounts.Crosspost"/> /
        /// <see cref="SpoolArticleRejectionCategory.Crosspost"/>).
        /// </param>
        /// <param name="other">
        /// Other rejections (<see cref="SpoolThroughputFeedCounts.Other"/> /
        /// <see cref="SpoolArticleRejectionCategory.Other"/>), including spam, size, queue, and write failures.
        /// </param>
        /// <remarks>
        /// <para>
        /// Source-generated by <see cref="LoggerMessageAttribute"/> at EventId <c>1</c>,
        /// <see cref="LogLevel.Information"/>. Message template:
        /// <c>Spool throughput (60s): processed={Processed}/min accepted={Accepted} rejected={Rejected} header={Header} crc={Crc} crosspost={Crosspost} other={Other}</c>.
        /// </para>
        /// <para>
        /// Called only from <see cref="EmitSnapshot"/> after the global activity gate passes. Does not include a
        /// <c>feed=</c> token; per-feed detail is emitted by <see cref="LogFeedThroughput"/>.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Spool throughput (60s): processed={Processed}/min accepted={Accepted} rejected={Rejected} header={Header} crc={Crc} crosspost={Crosspost} other={Other}")]
        private static partial void LogGlobalThroughput(
            ILogger logger,
            long processed,
            long accepted,
            long rejected,
            long header,
            long crc,
            long crosspost,
            long other);

        /// <summary>
        /// Logs spool throughput for one incoming feed during the last minute.
        /// </summary>
        /// <param name="logger">Host category logger receiving the formatted line.</param>
        /// <param name="feed">
        /// Incoming feed identifier from <see cref="SpoolThroughputFeedCounts.Feed"/> (for example <c>Giganews</c>,
        /// <c>local</c>, or <c>?</c>).
        /// </param>
        /// <param name="processed">
        /// Processed articles for the feed in the window (<see cref="SpoolThroughputFeedCounts.Processed"/>). Rendered with
        /// a <c>/min</c> suffix in the message template.
        /// </param>
        /// <param name="accepted">
        /// Accepted articles for the feed (<see cref="SpoolThroughputFeedCounts.Accepted"/>).
        /// </param>
        /// <param name="rejected">
        /// Total rejected articles for the feed (<see cref="SpoolThroughputFeedCounts.Rejected"/>).
        /// </param>
        /// <param name="header">
        /// Header-syntax rejections for the feed (<see cref="SpoolThroughputFeedCounts.HeaderSyntax"/>).
        /// </param>
        /// <param name="crc">yEnc CRC rejections for the feed (<see cref="SpoolThroughputFeedCounts.Crc"/>).</param>
        /// <param name="crosspost">
        /// Crosspost rejections for the feed (<see cref="SpoolThroughputFeedCounts.Crosspost"/>).
        /// </param>
        /// <param name="other">Other rejections for the feed (<see cref="SpoolThroughputFeedCounts.Other"/>).</param>
        /// <remarks>
        /// <para>
        /// Source-generated by <see cref="LoggerMessageAttribute"/> at EventId <c>2</c>,
        /// <see cref="LogLevel.Information"/>. Message template:
        /// <c>Spool throughput (60s) feed={Feed}: processed={Processed}/min accepted={Accepted} rejected={Rejected} header={Header} crc={Crc} crosspost={Crosspost} other={Other}</c>.
        /// </para>
        /// <para>
        /// Called from <see cref="EmitSnapshot"/> for each row in <see cref="SpoolThroughputMinuteSnapshot.Feeds"/>. The
        /// synthetic global rollup row (<see cref="SpoolThroughputMinuteSnapshot.GlobalFeedLabel"/>) is not passed to this
        /// helper.
        /// </para>
        /// </remarks>
        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Spool throughput (60s) feed={Feed}: processed={Processed}/min accepted={Accepted} rejected={Rejected} header={Header} crc={Crc} crosspost={Crosspost} other={Other}")]
        private static partial void LogFeedThroughput(
            ILogger logger,
            string feed,
            long processed,
            long accepted,
            long rejected,
            long header,
            long crc,
            long crosspost,
            long other);
    }
}
