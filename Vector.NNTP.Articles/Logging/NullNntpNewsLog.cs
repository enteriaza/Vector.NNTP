// <copyright file="NullNntpNewsLog.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: no-op INN news log for unit tests and DI fallbacks without host configuration.

using Vector.NNTP.Articles.Storage;

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Null-object implementation of <see cref="INntpNewsLog"/> that discards all accept, reject, cancel, and junk events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Role:</b> Satisfies the <see cref="INntpNewsLog"/> contract where INN <c>pathlog/news</c> output is unnecessary —
    /// unit tests, benchmarks, and DI graphs that exercise the transit spool without a configured log directory.
    /// </para>
    /// <para>
    /// <b>Registration:</b> <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>
    /// registers <see cref="Instance"/> as the <see cref="INntpNewsLog"/> singleton when
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> is not supplied. Production NNTPD hosts pass
    /// configuration and receive <see cref="NntpNewsLog"/> instead.
    /// </para>
    /// <para>
    /// <b>Direct test wiring:</b> Article and storage tests inject <see cref="Instance"/> into
    /// <see cref="NntpSpoolTransitStorage"/> and <see cref="NntpSpoolWriterPump"/> without going through DI when news
    /// file output is not under test.
    /// </para>
    /// <para><b>Behavioral contract (contrast with <see cref="NntpNewsLog"/>):</b></para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// All four interface methods are no-ops: no Serilog logger is created, no <c>news</c> rolling file is opened, and no
    /// lines are formatted through <see cref="NntpNewsLogFormatter"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Never throws — including for <see langword="null"/> or empty <c>messageId</c> values that
    /// <see cref="NntpNewsLog"/> rejects with <see cref="ArgumentException"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Does not resolve feeds via <see cref="NntpNewsFeedResolver"/>, parse cancel targets via
    /// <see cref="CancelControlHeaderParser"/>, or sanitize rejection reasons.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Does not implement <see cref="IDisposable"/>; there is no file sink to flush on host shutdown.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Parameters are read only to satisfy analyzer discard rules (<c>IDE0058</c>); callers must not depend on side
    /// effects from this implementation.
    /// </para>
    /// <para><b>Thread safety:</b> Stateless after construction; safe for concurrent writer pumps.</para>
    /// </remarks>
    internal sealed class NullNntpNewsLog : INntpNewsLog
    {
        /// <summary>
        /// Gets the shared singleton no-op logger used for DI fallback and direct test wiring.
        /// </summary>
        /// <value>
        /// A single process-wide <see cref="NullNntpNewsLog"/> instance created at type initialization.
        /// </value>
        /// <remarks>
        /// <para>
        /// Prefer this property over allocating additional <see cref="NullNntpNewsLog"/> instances so tests and fallback
        /// registration share one object identity and avoid redundant allocations.
        /// </para>
        /// <para>
        /// Registered by <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/> when
        /// configuration is absent.
        /// </para>
        /// </remarks>
        internal static NullNntpNewsLog Instance { get; } = new();

        /// <summary>
        /// Discards a spool commit accept event that would otherwise emit an INN plus line.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID from the transit command or local POST. Ignored; not validated (contrast
        /// <see cref="NntpNewsLog.LogAccepted"/>, which throws when null or empty).
        /// </param>
        /// <param name="origin">
        /// <see cref="NntpSpoolArticleOrigin"/> captured at enqueue. Ignored; feed resolution does not run.
        /// </param>
        /// <param name="articleBytes">
        /// Committed article bytes after preprocessing and postprocessing. Ignored; byte count and Path feed fallback are
        /// not evaluated.
        /// </param>
        /// <remarks>
        /// <para>
        /// Mirrors the call signature of <see cref="INntpNewsLog.LogAccepted"/>. On the production path,
        /// <see cref="NntpSpoolWriterPump"/> invokes this after successful
        /// <see cref="Utilities.IO.FileIOUtilities.AtomicWriteAsync"/>; cancel articles may also trigger
        /// <see cref="LogCancelProcessed"/> afterward, which is likewise discarded here.
        /// </para>
        /// <para>Never throws. Does not invoke <see cref="NntpNewsLogFormatter"/> or touch the filesystem.</para>
        /// </remarks>
        public void LogAccepted(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            _ = messageId;
            _ = origin;
            _ = articleBytes;
        }

        /// <summary>
        /// Discards a pipeline or storage rejection that would otherwise emit an INN minus line with a reason.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID from the transit command or local POST. Ignored; not validated (contrast
        /// <see cref="NntpNewsLog.LogRejected"/>, which throws when null or empty).
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution on real loggers. Ignored.
        /// </param>
        /// <param name="articleBytes">
        /// Article bytes available at rejection time for Path feed fallback on real loggers. Ignored; may be empty on
        /// enqueue-time rejections in <see cref="NntpSpoolTransitStorage"/>.
        /// </param>
        /// <param name="reason">
        /// Operator-facing rejection text (preprocess, postprocess, queue full, max size, write failure, and similar).
        /// Ignored; not sanitized or persisted.
        /// </param>
        /// <remarks>
        /// <para>
        /// Mirrors the call signature of <see cref="INntpNewsLog.LogRejected"/>. Called from
        /// <see cref="NntpSpoolTransitStorage"/> for enqueue rejections and from
        /// <see cref="NntpSpoolWriterPump"/> for preprocess, postprocess, and spool write failures on production hosts.
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        public void LogRejected(
            string messageId,
            in NntpSpoolArticleOrigin origin,
            ReadOnlySpan<byte> articleBytes,
            string reason)
        {
            _ = messageId;
            _ = origin;
            _ = articleBytes;
            _ = reason;
        }

        /// <summary>
        /// Discards a processed cancel event that would otherwise emit an INN cancel line after spool commit.
        /// </summary>
        /// <param name="messageId">
        /// Cancel article Message-ID (the control message itself). Ignored; not validated (contrast
        /// <see cref="NntpNewsLog.LogCancelProcessed"/>, which throws when null or empty).
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution on real loggers. Ignored.
        /// </param>
        /// <param name="articleBytes">
        /// Committed cancel article bytes used on real loggers for Path feed fallback and
        /// <see cref="CancelControlHeaderParser"/> target extraction. Ignored.
        /// </param>
        /// <remarks>
        /// <para>
        /// Mirrors the call signature of <see cref="INntpNewsLog.LogCancelProcessed"/>. On production hosts,
        /// <see cref="NntpSpoolWriterPump"/> calls this after successful spool write when postprocessing classified the
        /// article as a cancel, following <see cref="LogAccepted"/> for the same article.
        /// </para>
        /// <para>
        /// Never throws. Does not parse <c>Control</c> headers or emit cancel targets such as
        /// <c>Cancelling &lt;target&gt;</c>.
        /// </para>
        /// </remarks>
        public void LogCancelProcessed(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            _ = messageId;
            _ = origin;
            _ = articleBytes;
        }

        /// <summary>
        /// Discards a junk-newsgroup accept event that would otherwise emit an INN junk line.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID. Ignored; not validated (contrast <see cref="NntpNewsLog.LogJunked"/>, which throws when null
        /// or empty).
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution on real loggers. Ignored.
        /// </param>
        /// <param name="articleBytes">
        /// Article bytes used for Path feed fallback and byte count on real loggers. Ignored.
        /// </param>
        /// <remarks>
        /// <para>
        /// Mirrors the call signature of <see cref="INntpNewsLog.LogJunked"/>. Provided for contract completeness;
        /// transit spool production code does not invoke this method in v1 (spam, yEnc, and other policy rejections use
        /// <see cref="LogRejected"/> instead).
        /// </para>
        /// <para>Never throws.</para>
        /// </remarks>
        public void LogJunked(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            _ = messageId;
            _ = origin;
            _ = articleBytes;
        }
    }
}
