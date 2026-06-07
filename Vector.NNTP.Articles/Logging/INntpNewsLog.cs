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
    /// Transit spool components emit pipeline outcomes through this abstraction so storage and writer pumps stay
    /// decoupled from Serilog and file layout. Production hosts register <see cref="NntpNewsLog"/> (file sink at
    /// <c>{LogDir}/news</c>); unit tests and DI setups without <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// receive <see cref="NullNntpNewsLog.Instance"/> from
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>.
    /// </para>
    /// <para><b>Call sites:</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolTransitStorage"/> — minus lines for enqueue-time rejections (for example max article size and
    /// queue full).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="NntpSpoolWriterPump"/> — minus lines for preprocess, postprocess, and spool write failures; plus and
    /// optional cancel lines only after successful <see cref="FileIOUtilities.AtomicWriteAsync"/>.
    /// </description>
    /// </item>
    /// </list>
    /// <para><b>Line prefixes (INN semantics):</b></para>
    /// <list type="bullet">
    /// <item><description><c>+</c> — accepted article after durable spool commit (<see cref="LogAccepted"/>).</description></item>
    /// <item><description><c>-</c> — rejected article with operator-facing reason (<see cref="LogRejected"/>).</description></item>
    /// <item><description><c>c</c> — cancel control message processed after commit (<see cref="LogCancelProcessed"/>).</description></item>
    /// <item><description><c>j</c> — article accepted into junk newsgroup (<see cref="LogJunked"/>; reserved in v1).</description></item>
    /// </list>
    /// <para>
    /// Implementations log only final Articles pipeline outcomes. Accept and cancel lines are emitted after successful spool
    /// commit, not on wire <c>239</c>/<c>235</c> responses. Rejections (including yEnc and spam postprocess failures) use
    /// minus lines, not junk lines.
    /// </para>
    /// <para>
    /// Production formatting uses <see cref="NntpNewsLogFormatter"/> with event time
    /// (<see cref="DateTimeOffset.Now"/> at call time) and feed resolution via
    /// <see cref="NntpNewsFeedResolver"/>.
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
        /// Article Message-ID from the transit command or local POST. Callers must supply a non-empty value;
        /// <see cref="NntpNewsLog"/> validates and throws when it is missing.
        /// </param>
        /// <param name="origin">
        /// <see cref="NntpSpoolArticleOrigin"/> captured at enqueue. Used to resolve the feed column (local post, transit peer
        /// name, Path first hop, or peer hostname).
        /// </param>
        /// <param name="articleBytes">
        /// Committed article bytes after preprocessing and postprocessing. Supplies Path feed fallback when origin metadata is
        /// insufficient and provides the byte count on the plus line.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="NntpSpoolWriterPump"/> only after spool file write succeeds. Does not represent wire-level
        /// acceptance responses.
        /// </para>
        /// <para>
        /// When the committed article is a cancel control message, the pump also calls <see cref="LogCancelProcessed"/> after
        /// this method (plus line first, then cancel line).
        /// </para>
        /// </remarks>
        public void LogAccepted(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes);

        /// <summary>
        /// Records a rejected article, producing an INN minus line with a sanitized operator-facing reason.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID from the transit command or local POST. Callers must supply a non-empty value;
        /// <see cref="NntpNewsLog"/> validates and throws when it is missing.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Article bytes available at rejection time for Path feed fallback. May be empty when rejection occurs before a full
        /// payload is retained (for example early enqueue rejections in <see cref="NntpSpoolTransitStorage"/>).
        /// </param>
        /// <param name="reason">
        /// Operator-facing rejection text (preprocess failure, postprocess failure including spam and yEnc policy, queue full,
        /// max size, or a brief write-failure label). <see cref="NntpNewsLog"/> passes this through
        /// <see cref="NntpNewsLogFormatter.SanitizeReason"/> before formatting.
        /// </param>
        /// <remarks>
        /// <para>
        /// Called from <see cref="NntpSpoolTransitStorage"/> for enqueue rejections and from
        /// <see cref="NntpSpoolWriterPump"/> for preprocess, postprocess, and spool write failures.
        /// </para>
        /// <para>
        /// Null or whitespace reasons are formatted as the literal <c>Rejected</c> by
        /// <see cref="NntpNewsLogFormatter.FormatRejected"/>.
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
        /// Cancel article Message-ID (the control message itself, not the target article). Callers must supply a non-empty
        /// value; <see cref="NntpNewsLog"/> validates and throws when it is missing.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Committed cancel article bytes used for Path feed fallback and for parsing the cancel target Message-ID from
        /// <c>Control</c> headers via <see cref="CancelControlHeaderParser"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="NntpSpoolWriterPump"/> after successful spool write when postprocessing classified the
        /// article as a cancel. The pump emits <see cref="LogAccepted"/> for the cancel article before this method.
        /// </para>
        /// <para>
        /// When the cancel target cannot be parsed, production formatting logs <c>Cancelling ?</c> using
        /// <see cref="NntpNewsLogFeedNames.UnknownFeed"/> as the target token.
        /// </para>
        /// </remarks>
        public void LogCancelProcessed(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes);

        /// <summary>
        /// Records an article accepted into a junk newsgroup, producing an INN junk line.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID. Callers must supply a non-empty value; <see cref="NntpNewsLog"/> validates and throws when it
        /// is missing.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Article bytes used for Path feed fallback and as the byte count on the junk line.
        /// </param>
        /// <remarks>
        /// <para>
        /// Reserved for future junk-newsgroup filing. Transit spool production code does not invoke this method in v1; spam,
        /// yEnc, and other policy rejections use <see cref="LogRejected"/> instead.
        /// </para>
        /// <para>
        /// <see cref="NntpNewsLogFormatter.FormatJunked"/> is implemented and unit-tested so junk logging can be wired without
        /// formatter changes later.
        /// </para>
        /// </remarks>
        public void LogJunked(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes);
    }
}
