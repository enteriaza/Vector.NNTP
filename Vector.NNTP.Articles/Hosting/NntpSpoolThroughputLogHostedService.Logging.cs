// <copyright file="NntpSpoolThroughputLogHostedService.Logging.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// EventIds 1-2 (spool throughput minute summaries to the main host application log).

using Vector.NNTP.Articles.Metrics;

namespace Vector.NNTP.Articles.Hosting
{
    /// <summary>
    /// Source-generated <see cref="LoggerMessageAttribute"/> helpers for spool throughput minute summaries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emits single-line <see cref="LogLevel.Information"/> records to the main host logger
    /// (<c>NNTPD-.log</c> / console). Never writes to the INN <c>news-{date}.log</c> file.
    /// </para>
    /// </remarks>
    internal static partial class NntpSpoolThroughputLog
    {
        /// <summary>
        /// Emits global and per-feed throughput lines for a minute snapshot when activity occurred.
        /// </summary>
        /// <param name="logger">Host category logger (not <see cref="Logging.INntpNewsLog"/>).</param>
        /// <param name="snapshot">Delta snapshot from <see cref="NntpSpoolMetrics.TakeMinuteSnapshotAndReset"/>.</param>
        /// <remarks>
        /// Skips logging entirely when <see cref="SpoolThroughputFeedCounts.Processed"/> on the global row is zero.
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
        /// Logs the global spool throughput rollup for the last minute.
        /// </summary>
        /// <param name="logger">Host category logger.</param>
        /// <param name="processed">Total processed articles in the window.</param>
        /// <param name="accepted">Accepted articles in the window.</param>
        /// <param name="rejected">Total rejected articles in the window.</param>
        /// <param name="header">Header syntax rejections in the window.</param>
        /// <param name="crc">CRC rejections in the window.</param>
        /// <param name="crosspost">Crosspost rejections in the window.</param>
        /// <param name="other">Other rejections in the window.</param>
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
        /// Logs spool throughput for one feed during the last minute.
        /// </summary>
        /// <param name="logger">Host category logger.</param>
        /// <param name="feed">Incoming feed identifier.</param>
        /// <param name="processed">Processed articles for the feed in the window.</param>
        /// <param name="accepted">Accepted articles for the feed in the window.</param>
        /// <param name="rejected">Rejected articles for the feed in the window.</param>
        /// <param name="header">Header syntax rejections for the feed in the window.</param>
        /// <param name="crc">CRC rejections for the feed in the window.</param>
        /// <param name="crosspost">Crosspost rejections for the feed in the window.</param>
        /// <param name="other">Other rejections for the feed in the window.</param>
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
