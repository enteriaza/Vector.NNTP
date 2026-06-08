// <copyright file="INntpNewsLog.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: INN pathlog/news logging contract for transit spool accept/reject events.

using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Contract for writing INN-style accept, reject, cancel, and junk lines to the dedicated <c>news</c> log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Decouples transit spool storage and writer pumps from Serilog, file layout, and INN line formatting.
    /// Pipeline components depend on this abstraction rather than <see cref="NntpNewsLog"/> directly so unit tests can inject
    /// <see cref="NullNntpNewsLog.Instance"/> without creating log files.
    /// </para>
    /// <para><b>Implementations:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="NntpNewsLog"/> — production Serilog rolling file at <c>{LogDir}/news-{yyyyMMdd}.log</c>. Validates
    /// non-empty <c>messageId</c>, resolves feeds, formats lines, and writes at Information level. Also implements
    /// <see cref="IDisposable"/> for sink flush on host shutdown (not part of this interface).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NullNntpNewsLog"/> — no-op for tests and DI without
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>. Never throws; ignores all parameters.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Registration:</b> <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>
    /// registers <see cref="NntpNewsLog"/> when configuration is supplied, otherwise
    /// <see cref="NullNntpNewsLog.Instance"/>.
    /// </para>
    /// <para><b>Call sites:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolTransitStorage"/> — <see cref="LogRejected"/> for enqueue-time rejections (max article size,
    /// queue full).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriterPump"/> — <see cref="LogRejected"/> for preprocess, postprocess, and write failures;
    /// <see cref="LogAccepted"/> and optional <see cref="LogCancelProcessed"/> only after successful
    /// <see cref="FileIOUtilities.AtomicWriteAsync"/>.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Metrics alignment:</b> On the pump path, <see cref="Metrics.NntpSpoolMetrics.RecordArticleAccepted"/> or
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleRejected"/> runs immediately before the matching
    /// <see cref="LogAccepted"/> or <see cref="LogRejected"/> call so OpenTelemetry <c>feed</c> tags match the news line.
    /// Cancel lines have no separate meter in v1.
    /// </para>
    /// <para><b>Line prefixes (INN semantics):</b></para>
    /// <list type="bullet">
    /// <item><description><c>+</c> — accepted article after durable spool commit (<see cref="LogAccepted"/>).</description></item>
    /// <item><description><c>-</c> — rejected article with operator-facing reason (<see cref="LogRejected"/>).</description></item>
    /// <item><description><c>c</c> — cancel control message processed after commit (<see cref="LogCancelProcessed"/>).</description></item>
    /// <item><description><c>j</c> — article accepted into junk newsgroup (<see cref="LogJunked"/>; reserved in v1).</description></item>
    /// </list>
    /// <para>
    /// Implementations record final Articles pipeline outcomes only. Accept and cancel lines are emitted after successful
    /// spool commit, not on wire <c>239</c> or <c>235</c> responses. Policy rejections (spam, yEnc, header syntax, and
    /// similar) use minus lines via <see cref="LogRejected"/>, not junk lines.
    /// </para>
    /// <para>
    /// Production formatting (<see cref="NntpNewsLog"/>) uses <see cref="NntpNewsLogFormatter"/> with event time
    /// (<see cref="DateTimeOffset.Now"/> at call time) and feed resolution via
    /// <see cref="NntpNewsFeedResolver.ResolveFeed"/>.
    /// </para>
    /// <para>
    /// <b>Thread safety:</b> Implementations must tolerate concurrent calls from multiple writer pump threads.
    /// <see cref="NntpNewsLog"/> relies on Serilog async file sinks; <see cref="NullNntpNewsLog"/> is stateless.
    /// </para>
    /// </remarks>
    internal interface INntpNewsLog
    {
        /// <summary>
        /// Records an accepted article after durable spool write, producing an INN plus line.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID from the transit command or local POST. Callers must supply a non-null, non-empty value.
        /// <see cref="NntpNewsLog"/> throws <see cref="ArgumentException"/> when it is missing;
        /// <see cref="NullNntpNewsLog"/> ignores invalid values.
        /// </param>
        /// <param name="origin">
        /// <see cref="NntpSpoolArticleOrigin"/> captured at enqueue. Passed to
        /// <see cref="NntpNewsFeedResolver.ResolveFeed"/> for the feed column (local post, transit peer name, Path first
        /// hop, peer hostname, or <see cref="NntpNewsLogFeedNames.UnknownFeed"/>).
        /// </param>
        /// <param name="articleBytes">
        /// Committed article bytes after preprocessing and postprocessing on the pump path
        /// (<c>postprocessResult.ArticleBytes</c>). Supplies Path feed fallback when origin metadata is insufficient and
        /// provides <c>articleBytes.Length</c> as the size column on the plus line. May include local <c>Path</c> hop
        /// prepends from <see cref="Processing.ArticlePathHeaderMutator"/> when configured.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="NntpSpoolWriterPump"/> only after
        /// <see cref="FileIOUtilities.AtomicWriteAsync"/> succeeds, immediately after
        /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleAccepted"/>. Does not represent wire-level <c>239</c>
        /// acceptance.
        /// </para>
        /// <para>
        /// When postprocessing classified the article as a cancel
        /// (<see cref="Classification.ArticleTypeFlags.Cancel"/>), the pump also calls <see cref="LogCancelProcessed"/>
        /// for the same article immediately after this method (plus line first, then cancel line).
        /// </para>
        /// <para>
        /// <see cref="NntpNewsLog"/> does not throw when <paramref name="articleBytes"/> is empty; size zero is logged if
        /// supplied.
        /// </para>
        /// </remarks>
        public void LogAccepted(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes);

        /// <summary>
        /// Records a rejected article, producing an INN minus line with a sanitized operator-facing reason.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID from the transit command or local POST. Callers must supply a non-null, non-empty value.
        /// <see cref="NntpNewsLog"/> throws <see cref="ArgumentException"/> when it is missing;
        /// <see cref="NullNntpNewsLog"/> ignores invalid values.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver.ResolveFeed"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Article bytes available at rejection time for Path feed fallback. Enqueue rejections in
        /// <see cref="NntpSpoolTransitStorage"/> may pass empty bytes when no payload is retained. Preprocess failures in
        /// <see cref="NntpSpoolWriterPump"/> pass original enqueued bytes; postprocess and write failures pass preprocess
        /// output.
        /// </param>
        /// <param name="reason">
        /// Operator-facing rejection text: preprocess failure, postprocess failure (including spam and yEnc policy), queue
        /// full, max article size, or a brief write-failure label (often an exception type name). On
        /// <see cref="NntpNewsLog"/>, passed through <see cref="NntpNewsLogFormatter.SanitizeReason"/> before formatting.
        /// </param>
        /// <remarks>
        /// <para>
        /// Called from <see cref="NntpSpoolTransitStorage"/> for enqueue rejections and from
        /// <see cref="NntpSpoolWriterPump"/> for preprocess, postprocess, and spool write failures, immediately after the
        /// matching <see cref="Metrics.NntpSpoolMetrics.RecordArticleRejected"/> call on the pump path.
        /// </para>
        /// <para>
        /// Null or whitespace <paramref name="reason"/> values are formatted as the literal <c>Rejected</c> by
        /// <see cref="NntpNewsLogFormatter.FormatRejected"/> on production implementations.
        /// </para>
        /// <para>
        /// Rejection category for metrics (<see cref="Metrics.SpoolArticleRejectionClassifier"/>) is classified separately
        /// before the news log call; this method receives only the human-readable reason string.
        /// </para>
        /// </remarks>
        public void LogRejected(
            string messageId,
            in NntpSpoolArticleOrigin origin,
            ReadOnlySpan<byte> articleBytes,
            string reason);

        /// <summary>
        /// Records a processed cancel control article after durable spool commit, producing an INN cancel line.
        /// </summary>
        /// <param name="messageId">
        /// Cancel article Message-ID (the control message itself, not the target article). Callers must supply a non-null,
        /// non-empty value. <see cref="NntpNewsLog"/> throws <see cref="ArgumentException"/> when it is missing;
        /// <see cref="NullNntpNewsLog"/> ignores invalid values.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver.ResolveFeed"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Committed cancel article bytes after preprocessing and postprocessing. Used for Path feed fallback and for parsing
        /// the cancelled target Message-ID via <see cref="CancelControlHeaderParser.TryParseCancelTarget"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="NntpSpoolWriterPump"/> after successful spool write when postprocessing set
        /// <see cref="Classification.ArticleTypeFlags.Cancel"/>. The pump emits <see cref="LogAccepted"/> for the same
        /// article immediately before this method.
        /// </para>
        /// <para>
        /// On <see cref="NntpNewsLog"/>, when <see cref="CancelControlHeaderParser.TryParseCancelTarget"/> returns
        /// <see langword="false"/>, the implementation substitutes <see cref="NntpNewsLogFeedNames.UnknownFeed"/> so
        /// <see cref="NntpNewsLogFormatter.FormatCancelProcessed"/> emits <c>Cancelling ?</c> without angle brackets.
        /// </para>
        /// <para>There is no separate OpenTelemetry counter for cancel lines in v1.</para>
        /// </remarks>
        public void LogCancelProcessed(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes);

        /// <summary>
        /// Records an article accepted into a junk newsgroup, producing an INN junk line.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID. Callers must supply a non-null, non-empty value. <see cref="NntpNewsLog"/> throws
        /// <see cref="ArgumentException"/> when it is missing; <see cref="NullNntpNewsLog"/> ignores invalid values.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver.ResolveFeed"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Article bytes used for Path feed fallback and as <c>articleBytes.Length</c> on the junk line when junk filing is
        /// wired.
        /// </param>
        /// <remarks>
        /// <para>
        /// Reserved for future junk-newsgroup filing. Transit spool production code does not invoke this method in v1; spam,
        /// yEnc, and other policy rejections use <see cref="LogRejected"/> instead.
        /// </para>
        /// <para>
        /// <see cref="NntpNewsLogFormatter.FormatJunked"/> and <see cref="NntpNewsLog.LogJunked"/> are implemented and
        /// unit-tested so junk logging can be wired without formatter or contract changes later.
        /// </para>
        /// </remarks>
        public void LogJunked(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes);
    }
}
