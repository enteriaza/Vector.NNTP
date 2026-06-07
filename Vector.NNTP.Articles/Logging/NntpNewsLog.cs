// <copyright file="NntpNewsLog.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Serilog-backed INN pathlog/news writer for transit spool accept/reject events.

using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Vector.NNTP.Articles.Storage;
using Vector.NNTP.Utilities.Diagnostics;
using Vector.NNTP.Utilities.IO;

namespace Vector.NNTP.Articles.Logging
{
    /// <summary>
    /// Production <see cref="INntpNewsLog"/> implementation that writes INN-style lines to a dedicated Serilog rolling file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as a singleton by <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/>
    /// when host <see cref="IConfiguration"/> is supplied. Resolves the log directory with
    /// <see cref="LoggingDirectoryUtilities.ResolveLogDirectory"/> (same <c>Logging:LogDir</c> key as the NNTPD host) and
    /// writes to <c>{LogDir}/news</c> with Serilog day-based rolling file suffixes.
    /// </para>
    /// <para><b>Sink parameters</b> mirror the NNTPD host file sink in <c>Program.Serilog.cs</c>:</para>
    /// <list type="bullet">
    /// <item><description><see cref="RollingInterval.Day"/> rolling.</description></item>
    /// <item><description><see cref="RetainedFileCountLimit"/> (21) retained files.</description></item>
    /// <item><description><see cref="FlushToDiskInterval"/> (one second) flush interval.</description></item>
    /// <item><description>Buffered async file writes; no size-based roll.</description></item>
    /// <item><description>Output template <c>{Message}{NewLine}</c> only — no timestamp or level prefix from Serilog.</description></item>
    /// <item><description>No console sink (file-only news log).</description></item>
    /// </list>
    /// <para>
    /// Each log method builds a complete INN line via <see cref="NntpNewsLogFormatter"/> using
    /// <see cref="DateTimeOffset.Now"/> at call time (event time), resolves feed names with
    /// <see cref="NntpNewsFeedResolver"/>, and writes the formatted string at Serilog Information level with the INN
    /// line as the entire message body (no structured properties or Serilog timestamp prefix).
    /// </para>
    /// <para>
    /// Implements <see cref="IDisposable"/> so the dedicated <see cref="Serilog.Core.Logger"/> can flush and release file
    /// handles on host shutdown. Callers should dispose the singleton when tearing down the process.
    /// </para>
    /// <para><b>Thread safety:</b> Serilog file sinks are safe for concurrent writes from multiple writer pump threads.</para>
    /// </remarks>
    internal sealed class NntpNewsLog : INntpNewsLog, IDisposable
    {
        /// <summary>
        /// Serilog rolling file base name (without date suffix) under the resolved log directory.
        /// </summary>
        /// <remarks>
        /// Combined with <see cref="LoggingDirectoryUtilities.ResolveLogDirectory"/> as <c>{LogDir}/news</c>, matching INN
        /// <c>pathlog/news</c> naming. Serilog appends the day suffix when <see cref="RollingInterval.Day"/> is configured.
        /// </remarks>
        private const string NewsLogFileName = "news";

        /// <summary>
        /// Maximum number of rolled <c>news</c> log files retained on disk.
        /// </summary>
        /// <remarks>
        /// Matches <c>RetainedFileCountLimit</c> in NNTPD <c>Program.Serilog.cs</c> so operator retention policy is
        /// consistent across host and news logs.
        /// </remarks>
        private const int RetainedFileCountLimit = 21;

        /// <summary>
        /// Interval between forced flushes of the async news file sink buffer.
        /// </summary>
        /// <remarks>
        /// Matches <c>FlushToDiskInterval</c> in NNTPD <c>Program.Serilog.cs</c> (one second).
        /// </remarks>
        private static readonly TimeSpan FlushToDiskInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Dedicated Serilog core logger instance writing only INN news lines to the rolling file sink.
        /// </summary>
        /// <remarks>
        /// Created once in the constructor. Not the static <see cref="Log.Logger"/> used by the host application log.
        /// </remarks>
        private readonly Logger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpNewsLog"/> class and creates the rolling file sink.
        /// </summary>
        /// <param name="configuration">
        /// Host configuration supplying <c>Logging:LogDir</c> (and optional overrides read by
        /// <see cref="LoggingDirectoryUtilities.ResolveLogDirectory"/>).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configuration"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Configures minimum level <see cref="Serilog.Events.LogEventLevel.Information"/> for this logger only. Does not
        /// alter the host-wide Serilog pipeline.
        /// </para>
        /// <para>
        /// Wraps the file sink in Serilog.Sinks.Async so writer pump threads do not block on disk I/O during bursts.
        /// </para>
        /// </remarks>
        public NntpNewsLog(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            string logDirectory = LoggingDirectoryUtilities.ResolveLogDirectory(configuration);
            _logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Async(asyncConfig => asyncConfig.File(
                    path: Path.Combine(logDirectory, NewsLogFileName),
                    outputTemplate: "{Message}{NewLine}",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileCountLimit,
                    fileSizeLimitBytes: null,
                    rollOnFileSizeLimit: false,
                    buffered: true,
                    flushToDiskInterval: FlushToDiskInterval))
                .CreateLogger();
        }

        /// <summary>
        /// Writes a plus line after successful spool commit.
        /// </summary>
        /// <param name="messageId">Article Message-ID; must be non-empty.</param>
        /// <param name="origin">Enqueue origin metadata for feed resolution.</param>
        /// <param name="articleBytes">
        /// Committed article bytes used for Path feed fallback and as the byte count on the plus line.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="NntpSpoolWriterPump"/> only after
        /// <see cref="FileIOUtilities.AtomicWriteAsync"/> succeeds. Does not log wire-level
        /// <c>239</c> acceptance.
        /// </para>
        /// <para>Does not throw when <paramref name="articleBytes"/> is empty; size zero is logged if supplied.</para>
        /// </remarks>
        public void LogAccepted(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            string feed = NntpNewsFeedResolver.ResolveFeed(origin, articleBytes);
            string line = NntpNewsLogFormatter.FormatAccepted(
                DateTimeOffset.Now,
                feed,
                messageId,
                articleBytes.Length);
            _logger.Information(line);
        }

        /// <summary>
        /// Writes a minus line for a storage or writer-pipeline rejection.
        /// </summary>
        /// <param name="messageId">Article Message-ID; must be non-empty.</param>
        /// <param name="origin">Enqueue origin metadata for feed resolution.</param>
        /// <param name="articleBytes">
        /// Article bytes used for Path feed fallback when transit peer name and hostname are unavailable; may be empty.
        /// </param>
        /// <param name="reason">
        /// Operator-facing rejection reason; sanitized by <see cref="NntpNewsLogFormatter.SanitizeReason"/> before write.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Called from <see cref="NntpSpoolTransitStorage"/> (enqueue rejections) and
        /// <see cref="NntpSpoolWriterPump"/> (preprocess, postprocess, and write failures). Write failures may
        /// supply only an exception type name as <paramref name="reason"/>.
        /// </para>
        /// <para>
        /// A null or whitespace <paramref name="reason"/> is formatted as the literal <c>Rejected</c> by the formatter.
        /// </para>
        /// </remarks>
        public void LogRejected(
            string messageId,
            in NntpSpoolArticleOrigin origin,
            ReadOnlySpan<byte> articleBytes,
            string reason)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            string feed = NntpNewsFeedResolver.ResolveFeed(origin, articleBytes);
            string line = NntpNewsLogFormatter.FormatRejected(
                DateTimeOffset.Now,
                feed,
                messageId,
                reason);
            _logger.Information(line);
        }

        /// <summary>
        /// Writes a cancel line after a cancel article is committed to the spool.
        /// </summary>
        /// <param name="messageId">Cancel article Message-ID; must be non-empty.</param>
        /// <param name="origin">Enqueue origin metadata for feed resolution.</param>
        /// <param name="articleBytes">
        /// Committed cancel article bytes used for Path feed fallback and cancel target header parsing.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="NntpSpoolWriterPump"/> after successful spool write when the postprocessor
        /// classified the article as a cancel. Parses the target Message-ID with
        /// <see cref="CancelControlHeaderParser"/>; unparseable targets log as
        /// <c>Cancelling ?</c> via <see cref="NntpNewsLogFeedNames.UnknownFeed"/>.
        /// </para>
        /// <para>
        /// Emits a separate plus line for the cancel article before this cancel line is written by the pump caller
        /// sequence (plus first, then cancel).
        /// </para>
        /// </remarks>
        public void LogCancelProcessed(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            string feed = NntpNewsFeedResolver.ResolveFeed(origin, articleBytes);
            string target = CancelControlHeaderParser.TryParseCancelTarget(articleBytes, out string parsedTarget)
                ? parsedTarget
                : NntpNewsLogFeedNames.UnknownFeed;
            string line = NntpNewsLogFormatter.FormatCancelProcessed(
                DateTimeOffset.Now,
                feed,
                messageId,
                target);
            _logger.Information(line);
        }

        /// <summary>
        /// Writes a junk line for an article accepted into a junk newsgroup.
        /// </summary>
        /// <param name="messageId">Article Message-ID; must be non-empty.</param>
        /// <param name="origin">Enqueue origin metadata for feed resolution.</param>
        /// <param name="articleBytes">
        /// Article bytes used for Path feed fallback and as the byte count on the junk line.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Implemented for <see cref="INntpNewsLog"/> contract completeness. Transit spool production code does not call
        /// this method until junk-newsgroup filing exists; yEnc, spam, and other rejections use
        /// <see cref="LogRejected"/> instead.
        /// </para>
        /// </remarks>
        public void LogJunked(string messageId, in NntpSpoolArticleOrigin origin, ReadOnlySpan<byte> articleBytes)
        {
            ArgumentException.ThrowIfNullOrEmpty(messageId);
            string feed = NntpNewsFeedResolver.ResolveFeed(origin, articleBytes);
            string line = NntpNewsLogFormatter.FormatJunked(
                DateTimeOffset.Now,
                feed,
                messageId,
                articleBytes.Length);
            _logger.Information(line);
        }

        /// <summary>
        /// Flushes buffered news log output and releases the dedicated Serilog file sink.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Delegates to <see cref="Serilog.Core.Logger.Dispose"/>. Safe to call multiple times only if the underlying Serilog logger
        /// tolerates repeated disposal; hosts should dispose the singleton once at shutdown.
        /// </para>
        /// <para>After disposal, subsequent <see cref="INntpNewsLog"/> calls on this instance would fault the sink.</para>
        /// </remarks>
        public void Dispose()
        {
            _logger.Dispose();
        }
    }
}
