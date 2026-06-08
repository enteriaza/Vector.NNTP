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
    /// <b>Role:</b> File-backed news log for transit spool pipeline outcomes. Contrast
    /// <see cref="NullNntpNewsLog.Instance"/>, which satisfies the same contract without I/O when
    /// <see cref="DependencyInjection.ServiceCollectionExtensions.AddNntpArticlesTransitSpool"/> runs without
    /// <see cref="IConfiguration"/>.
    /// </para>
    /// <para>
    /// <b>Registration:</b> Singleton factory
    /// <c>sp =&gt; new NntpNewsLog(configuration)</c> when host configuration is supplied. Resolves the log directory with
    /// <see cref="LoggingDirectoryUtilities.ResolveLogDirectory"/> (same <c>Logging:LogDir</c> key as the NNTPD host) and
    /// writes to <c>{LogDir}/news-{yyyyMMdd}.log</c> (Serilog day rolling via the <see cref="NewsLogFileName"/> path
    /// template).
    /// </para>
    /// <para><b>Per-call pipeline (all four log methods):</b></para>
    /// <list type="number">
    /// <item><description>Validate <c>messageId</c> (non-null, non-empty).</description></item>
    /// <item><description>Resolve feed via <see cref="NntpNewsFeedResolver.ResolveFeed"/>.</description></item>
    /// <item>
    /// <description>
    /// Build one INN line through <see cref="NntpNewsLogFormatter"/> using <see cref="DateTimeOffset.Now"/> at call time.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Write the line at Serilog Information level with the INN text as the entire message body (no structured properties,
    /// no Serilog timestamp or level prefix).
    /// </description>
    /// </item>
    /// </list>
    /// <para><b>Sink parameters</b> mirror the NNTPD host file sink in <c>Program.Serilog.cs</c>:</para>
    /// <list type="bullet">
    /// <item><description><see cref="RollingInterval.Day"/> rolling.</description></item>
    /// <item><description><see cref="RetainedFileCountLimit"/> (21) retained files.</description></item>
    /// <item><description><see cref="FlushToDiskInterval"/> (one second) forced flush interval.</description></item>
    /// <item><description>Buffered async file writes via Serilog.Sinks.Async; no size-based roll.</description></item>
    /// <item><description>Output template <c>{Message}{NewLine}</c> only.</description></item>
    /// <item><description>No console sink (file-only news log).</description></item>
    /// </list>
    /// <para>
    /// <b>Metrics alignment:</b> <see cref="NntpSpoolWriterPump"/> records
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleAccepted"/> or
    /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleRejected"/> immediately before the matching news log call so
    /// OpenTelemetry <c>feed</c> tags match the formatted line.
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> Implements <see cref="IDisposable"/> so the dedicated Serilog <see cref="Serilog.Core.Logger"/> can flush and release
    /// file handles on host shutdown. The generic host disposes singletons that implement <see cref="IDisposable"/> when
    /// the process stops.
    /// </para>
    /// <para><b>Thread safety:</b> Serilog async file sinks tolerate concurrent Information writes from multiple writer pump threads.</para>
    /// </remarks>
    internal sealed partial class NntpNewsLog : INntpNewsLog, IDisposable
    {
        /// <summary>
        /// Serilog rolling file path template under the resolved log directory.
        /// </summary>
        /// <value>Literal <c>news-.log</c>.</value>
        /// <remarks>
        /// <para>
        /// Combined with <see cref="LoggingDirectoryUtilities.ResolveLogDirectory"/> as <c>{LogDir}/news-.log</c>. Serilog
        /// inserts the day stamp before the extension when <see cref="RollingInterval.Day"/> is configured, producing files
        /// such as <c>news-20260607.log</c> (matching the host <c>NNTPD-.log</c> rolling convention).
        /// </para>
        /// </remarks>
        private const string NewsLogFileName = "news-.log";

        /// <summary>
        /// Maximum number of rolled <c>news</c> log files retained on disk.
        /// </summary>
        /// <value>21 files.</value>
        /// <remarks>
        /// Passed to Serilog <c>retainedFileCountLimit</c>. Matches NNTPD <c>Program.Serilog.cs</c> so operator retention
        /// policy is consistent across host and news logs.
        /// </remarks>
        private const int RetainedFileCountLimit = 21;

        /// <summary>
        /// Interval between forced flushes of the async news file sink buffer.
        /// </summary>
        /// <value>One second.</value>
        /// <remarks>
        /// Passed to Serilog <c>flushToDiskInterval</c>. Matches NNTPD <c>Program.Serilog.cs</c>.
        /// </remarks>
        private static readonly TimeSpan FlushToDiskInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Dedicated Serilog core logger instance writing only INN news lines to the rolling file sink.
        /// </summary>
        private readonly Logger _logger;

        /// <summary>
        /// Host category logger for Serilog sink failures on the news log path.
        /// </summary>
        private readonly ILogger<NntpNewsLog> _hostLogger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpNewsLog"/> class and creates the rolling file sink.
        /// </summary>
        /// <param name="configuration">
        /// Host configuration supplying <c>Logging:LogDir</c> (and optional overrides read by
        /// <see cref="LoggingDirectoryUtilities.ResolveLogDirectory"/>). When unset, logs default under
        /// <c>{AppContext.BaseDirectory}/logs</c> per utilities policy.
        /// </param>
        /// <param name="hostLogger">
        /// Host category logger used when the Serilog news sink throws so pump workers do not fault on log I/O failures.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configuration"/> or <paramref name="hostLogger"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Configures minimum level <see cref="Serilog.Events.LogEventLevel.Information"/> for this logger only. Does not
        /// alter the host-wide Serilog pipeline.
        /// </para>
        /// <para>
        /// Wraps the file sink in Serilog.Sinks.Async so writer pump threads do not block on disk I/O during bursts.
        /// <c>fileSizeLimitBytes</c> is <see langword="null"/> and <c>rollOnFileSizeLimit</c> is
        /// <see langword="false"/> so rolls are day-based only.
        /// </para>
        /// </remarks>
        public NntpNewsLog(IConfiguration configuration, ILogger<NntpNewsLog> hostLogger)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(hostLogger);
            _hostLogger = hostLogger;
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
        /// <param name="messageId">
        /// Article Message-ID from the transit command or local POST. Must be non-null and non-empty.
        /// </param>
        /// <param name="origin">
        /// <see cref="NntpSpoolArticleOrigin"/> captured at enqueue; passed to <see cref="NntpNewsFeedResolver"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Committed article bytes after preprocessing and postprocessing. Supplies Path feed fallback when origin metadata
        /// is insufficient and provides <c>articleBytes.Length</c> as the size column on the plus line. On the pump path
        /// these are <c>postprocessResult.ArticleBytes</c> (may include local <c>Path</c> hop prepends from
        /// <see cref="Processing.ArticlePathHeaderMutator"/>).
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="NntpSpoolWriterPump"/> only after
        /// <see cref="FileIOUtilities.AtomicWriteAsync"/> succeeds, immediately after
        /// <see cref="Metrics.NntpSpoolMetrics.RecordArticleAccepted"/>. Does not log wire-level <c>239</c> acceptance.
        /// </para>
        /// <para>
        /// When the committed article is classified as a cancel, the pump also calls <see cref="LogCancelProcessed"/>
        /// afterward (plus line first, then cancel line).
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
            TryWriteLine(line);
        }

        /// <summary>
        /// Writes a formatted INN news line through Serilog, logging sink failures to the host logger without throwing.
        /// </summary>
        /// <param name="line">Preformatted INN line to write at Information level.</param>
        private void TryWriteLine(string line)
        {
            try
            {
                _logger.Information(line);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_hostLogger.IsEnabled(LogLevel.Warning))
                {
                    LogSinkFailure(_hostLogger, ex);
                }
            }
        }

        /// <summary>
        /// Writes a minus line for a storage or writer-pipeline rejection.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID from the transit command or local POST. Must be non-null and non-empty.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Article bytes available at rejection time for Path feed fallback. May be empty when rejection occurs before a
        /// full payload is retained (enqueue rejections in <see cref="NntpSpoolTransitStorage"/>). Preprocess failures pass
        /// original enqueued bytes; postprocess and write failures pass preprocess output.
        /// </param>
        /// <param name="reason">
        /// Operator-facing rejection text (preprocess failure, postprocess failure including spam and yEnc policy, queue
        /// full, max size, or a brief write-failure label). Passed through
        /// <see cref="NntpNewsLogFormatter.SanitizeReason"/> before formatting.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Called from <see cref="NntpSpoolTransitStorage"/> for enqueue rejections and from
        /// <see cref="NntpSpoolWriterPump"/> for preprocess, postprocess, and write failures, immediately after the
        /// matching <see cref="Metrics.NntpSpoolMetrics.RecordArticleRejected"/> call.
        /// </para>
        /// <para>
        /// Write failures typically supply only <see cref="Exception.GetType"/> name as <paramref name="reason"/>; full
        /// exception detail remains in application logs.
        /// </para>
        /// <para>
        /// Null or whitespace <paramref name="reason"/> is formatted as the literal <c>Rejected</c> by
        /// <see cref="NntpNewsLogFormatter.FormatRejected"/>.
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
            TryWriteLine(line);
        }

        /// <summary>
        /// Writes a cancel line after a cancel article is committed to the spool.
        /// </summary>
        /// <param name="messageId">
        /// Cancel article Message-ID (the control message itself, not the target article). Must be non-null and non-empty.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Committed cancel article bytes after preprocessing and postprocessing. Used for Path feed fallback and for
        /// <see cref="CancelControlHeaderParser.TryParseCancelTarget"/>.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Invoked from <see cref="NntpSpoolWriterPump"/> after successful spool write when postprocessing set
        /// <see cref="Classification.ArticleTypeFlags.Cancel"/>. The pump emits <see cref="LogAccepted"/> for the same
        /// article immediately before this method.
        /// </para>
        /// <para>
        /// Parses the cancelled target with <see cref="CancelControlHeaderParser.TryParseCancelTarget"/>. When parsing
        /// fails, substitutes <see cref="NntpNewsLogFeedNames.UnknownFeed"/> so
        /// <see cref="NntpNewsLogFormatter.FormatCancelProcessed"/> emits <c>Cancelling ?</c> without angle brackets.
        /// </para>
        /// <para>There is no separate OpenTelemetry counter for cancel lines in v1; only the plus accept line is metered.</para>
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
            TryWriteLine(line);
        }

        /// <summary>
        /// Writes a junk line for an article accepted into a junk newsgroup.
        /// </summary>
        /// <param name="messageId">
        /// Article Message-ID. Must be non-null and non-empty.
        /// </param>
        /// <param name="origin">
        /// Enqueue origin metadata for feed resolution via <see cref="NntpNewsFeedResolver"/>.
        /// </param>
        /// <param name="articleBytes">
        /// Article bytes used for Path feed fallback and as <c>articleBytes.Length</c> on the junk line.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="messageId"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Implemented for <see cref="INntpNewsLog"/> contract completeness. Transit spool production code does not call
        /// this method in v1; spam, yEnc, and other policy rejections use <see cref="LogRejected"/> instead.
        /// </para>
        /// <para>
        /// When wired, follows the same resolve-format-write pipeline as <see cref="LogAccepted"/> with a
        /// <c>j</c> prefix via <see cref="NntpNewsLogFormatter.FormatJunked"/>.
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
            TryWriteLine(line);
        }

        /// <summary>
        /// Flushes buffered news log output and releases the dedicated Serilog file sink.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements <see cref="IDisposable"/> by disposing <see cref="_logger"/>. Hosts should allow the
        /// singleton to be disposed once at shutdown so async buffers flush to disk.
        /// </para>
        /// <para>
        /// Repeated disposal behavior depends on Serilog internals; treat the instance as unusable after disposal.
        /// Subsequent <see cref="INntpNewsLog"/> calls would fault or no-op depending on sink state.
        /// </para>
        /// <para>Never throws under normal Serilog disposal semantics.</para>
        /// </remarks>
        public void Dispose()
        {
            _logger.Dispose();
        }
    }
}
