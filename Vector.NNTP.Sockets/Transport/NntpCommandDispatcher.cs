// <copyright file="NntpCommandDispatcher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: routes classified verbs to per-command handlers.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.Metrics;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;
using Vector.NNTP.Sockets.Tls;
using Vector.NNTP.Sockets.Transport.Commands;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Classifies incoming NNTP command lines and invokes the matching command handler after policy gates.
    /// </summary>
    /// <param name="auth">Authentication service for AUTHINFO and SASL continuations.</param>
    /// <param name="articleStorage">Optional reader article storage (GROUP, ARTICLE, POST, and related verbs).</param>
    /// <param name="transitStorage">Optional transit article storage for IHAVE and TAKETHIS bodies.</param>
    /// <param name="historyDatabase">Optional transit history database for CHECK, IHAVE, and TAKETHIS.</param>
    /// <param name="tlsCertificateSource">Optional TLS certificate source for STARTTLS.</param>
    /// <param name="scramCredentialStore">Optional SCRAM credential store for CAPABILITIES advertisement.</param>
    /// <param name="options">Server options including CPU overload rejection policy.</param>
    /// <param name="cpuLoadMonitor">CPU overload hysteresis gate consulted at the start of each command.</param>
    /// <param name="logger">Logger for redacted command and overload reject diagnostics.</param>
    /// <remarks>
    /// <para>
    /// <see cref="DispatchBytesAsync"/> is the hot entry point. When <see cref="NntpServerOptions.CpuRejectEnabled"/>
    /// and <see cref="INntpCpuLoadMonitor.IsOverloaded"/> are true, the dispatcher writes
    /// <c>400 Service temporarily unavailable</c> (RFC 3977 §3.2.1), sets
    /// <see cref="NntpSessionState.CpuOverloadCloseRequested"/>, and returns <see langword="false"/> so the
    /// session runner tears down the connection.
    /// </para>
    /// <para>
    /// Otherwise the flow is: SASL continuation handling, verb classification, <see cref="NntpCommandGate"/> policy
    /// checks, then a <c>switch</c> to per-command handlers under <c>Transport.Commands</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="auth"/>, <paramref name="options"/>, <paramref name="cpuLoadMonitor"/>, or
    /// <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public sealed class NntpCommandDispatcher(
        NntpAuthenticationService auth,
        INntpArticleStorage? articleStorage,
        INntpTransitStorage? transitStorage,
        IHistoryDatabase? historyDatabase,
        ITlsCertificateSource? tlsCertificateSource,
        IScramCredentialStore? scramCredentialStore,
        IOptions<NntpServerOptions> options,
        INntpCpuLoadMonitor cpuLoadMonitor,
        ILogger<NntpCommandDispatcher> logger)
    {
        /// <summary>
        /// Authentication coordinator for AUTHINFO and multi-step SASL exchanges.
        /// </summary>
        private readonly NntpAuthenticationService _auth = auth ?? throw new ArgumentNullException(nameof(auth));

        /// <summary>
        /// Reader storage adapter; <see langword="null"/> on transit-only hosts.
        /// </summary>
        private readonly INntpArticleStorage? _articleStorage = articleStorage;

        /// <summary>
        /// Transit storage adapter for streaming article bodies; <see langword="null"/> on reader-only hosts.
        /// </summary>
        private readonly INntpTransitStorage? _transitStorage = transitStorage;

        /// <summary>
        /// History database for CHECK/IHAVE/TAKETHIS deduplication; <see langword="null"/> when transit history is disabled.
        /// </summary>
        private readonly IHistoryDatabase? _historyDatabase = historyDatabase;

        /// <summary>
        /// TLS certificate source for STARTTLS; <see langword="null"/> when implicit TLS only or TLS is disabled.
        /// </summary>
        private readonly ITlsCertificateSource? _tlsCertificateSource = tlsCertificateSource;

        /// <summary>
        /// SCRAM credential store for CAPABILITIES mechanism advertisement; optional.
        /// </summary>
        private readonly IScramCredentialStore? _scramCredentialStore = scramCredentialStore;

        /// <summary>
        /// Bound <see cref="NntpServerOptions"/> including CPU reject thresholds and feature flags.
        /// </summary>
        private readonly IOptions<NntpServerOptions> _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>
        /// CPU overload gate read before each command dispatch.
        /// </summary>
        private readonly INntpCpuLoadMonitor _cpuLoadMonitor = cpuLoadMonitor ?? throw new ArgumentNullException(nameof(cpuLoadMonitor));

        /// <summary>
        /// Logger passed to <see cref="NntpCommandDispatcherLog"/> for redacted command and overload diagnostics.
        /// </summary>
        private readonly ILogger<NntpCommandDispatcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Dispatches a UTF-16 command line by encoding to ASCII and delegating to <see cref="DispatchBytesAsync"/>.
        /// </summary>
        /// <param name="session">Active NNTP session state and transport.</param>
        /// <param name="line">Command line text without trailing CRLF.</param>
        /// <param name="cancellationToken">Cancellation token for response I/O.</param>
        /// <returns>
        /// <see langword="true"/> when the command loop should continue;
        /// <see langword="false"/> when the session should end (QUIT, CPU overload close, or handler-requested quit).
        /// </returns>
        /// <remarks>
        /// Convenience wrapper for test and harness code paths. The session hot loop should call
        /// <see cref="DispatchBytesAsync"/> directly to avoid an extra <see cref="Encoding.ASCII"/> allocation.
        /// Non-ASCII code points in <paramref name="line"/> are replaced per <see cref="Encoding.ASCII"/> rules before classification.
        /// </remarks>
        internal async ValueTask<bool> DispatchAsync(
            NntpSession session,
            string line,
            CancellationToken cancellationToken)
        {
            return await DispatchBytesAsync(session, Encoding.ASCII.GetBytes(line), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispatches a classified NNTP verb to the appropriate handler after CPU, SASL, and policy gate checks.
        /// </summary>
        /// <param name="session">Active NNTP session state and transport.</param>
        /// <param name="lineBytes">Command line bytes without trailing CRLF.</param>
        /// <param name="cancellationToken">Cancellation token for response and body I/O.</param>
        /// <returns>
        /// <see langword="true"/> when the session command loop should continue;
        /// <see langword="false"/> when the connection should close after this response
        /// (for example QUIT handled, or CPU overload <c>400</c> with
        /// <see cref="NntpSessionState.CpuOverloadCloseRequested"/> set).
        /// </returns>
        /// <remarks>
        /// <para>Order of evaluation:</para>
        /// <list type="number">
        /// <item><description>CPU overload gate when enabled.</description></item>
        /// <item><description>Debug logging of the received line (redacted).</description></item>
        /// <item><description>SASL in-progress continuations (except AUTHINFO and QUIT).</description></item>
        /// <item><description>Verb classification and <see cref="NntpCommandGate"/> policy.</description></item>
        /// <item><description>Per-verb command handler dispatch.</description></item>
        /// </list>
        /// <para>
        /// Gate rejections on POST/TAKETHIS still drain a pipelined multi-line body before returning the
        /// policy response so the session stays synchronized with the client.
        /// </para>
        /// </remarks>
        internal async ValueTask<bool> DispatchBytesAsync(
            NntpSession session,
            ReadOnlyMemory<byte> lineBytes,
            CancellationToken cancellationToken)
        {
            string line = Encoding.ASCII.GetString(lineBytes.Span);
            session.Connection.BeginCommandDispatch();
            if (_options.Value.CpuRejectEnabled && _cpuLoadMonitor.IsOverloaded())
            {
                NntpCpuLoadSnapshot snapshot = _cpuLoadMonitor.GetSnapshot();
                NntpCommandDispatcherLog.LogCpuOverloadRejectCommand(
                    _logger,
                    session.Connection.ConnectionLogPrefix,
                    snapshot.EffectiveEwmaPercent,
                    snapshot.DominantSignal,
                    snapshot.ProcessEwmaPercent,
                    snapshot.HostEwmaPercent,
                    snapshot.CgroupEwmaPercent,
                    snapshot.GateState,
                    snapshot.RejectThresholdPercent,
                    snapshot.ResumeThresholdPercent);
                NntpServerLoadMetrics.RecordCommandReject();
                await session.Writer.WritePreencodedAsync(NntpPreencodedResponses.ServiceUnavailable400, cancellationToken)
                    .ConfigureAwait(false);
                session.State.CpuOverloadCloseRequested = true;
                return false;
            }

            if (!session.State.MultiLineBodyPending && !session.State.IsCompressionActive)
            {
                NntpCommandDispatcherLog.LogCommandReceived(_logger, session.Connection.ConnectionLogPrefix, FormatLineForLog(lineBytes));
            }
            if (session.State.AuthenticationState == Session.AuthenticationState.SaslInProgress &&
                !line.StartsWith("AUTHINFO", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    return await NntpCmdQuit.DispatchAsync(session, cancellationToken).ConfigureAwait(false);
                }

                await _auth.HandleSaslContinuationAsync(session, line.Trim(), cancellationToken).ConfigureAwait(false);
                return !session.State.QuitRequested;
            }

            NntpKnownVerb verb = NntpCommandVerbBytes.Classify(lineBytes.Span);
            if (verb == NntpKnownVerb.Unknown)
            {
                if (!session.State.IsCompressionActive)
                {
                    NntpCommandDispatcherLog.LogUnknownCommand(_logger, FormatLineForLog(lineBytes));
                }

                await session.Writer.WritePreencodedAsync(NntpPreencodedResponses.UnknownCommand500, cancellationToken).ConfigureAwait(false);
                return !session.State.QuitRequested;
            }

            NntpGateResult gate = NntpCommandGate.Evaluate(session, verb);
            if (gate != NntpGateResult.Allow)
            {
                // For multi-line commands, still need to consume the body due to pipelining
                if (verb is NntpKnownVerb.Post or NntpKnownVerb.Takethis)
                {
                    session.State.MultiLineBodyPending = true;
                    try
                    {
                        await NntpDotStuffingReader.DrainBodyAsync(session.LineReader, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        session.State.MultiLineBodyPending = false;
                    }
                }

                await NntpCommandGate.WriteRejectionAsync(session, gate, cancellationToken).ConfigureAwait(false);
                return !session.State.QuitRequested;
            }

            switch (verb)
            {
                case NntpKnownVerb.Quit:
                    return await NntpCmdQuit.DispatchAsync(session, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Help:
                    return await NntpCmdHelp.DispatchAsync(session, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Date:
                    return await NntpCmdDate.DispatchAsync(session, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Capabilities:
                    return await NntpCapabilities.DispatchAsync(session, _scramCredentialStore, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Mode:
                    return await NntpCmdMode.DispatchAsync(session, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Authinfo:
                    _ = await NntpCmdAuthinfo.DispatchAsync(session, _auth, line, cancellationToken).ConfigureAwait(false);
                    break;
                case NntpKnownVerb.Group:
                    return await NntpCmdGroup.DispatchAsync(session, _articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.List:
                    return await NntpCmdList.DispatchAsync(session, _articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.ListGroup:
                    return await NntpCmdListGroup.DispatchAsync(session, _articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Article:
                    return await NntpCmdArticle.DispatchAsync(
                        session,
                        _articleStorage,
                        NntpCommandLineHelpers.GetVerb(line),
                        line,
                        cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Post:
                    return await NntpCmdPost.DispatchAsync(session, _articleStorage, session.LineReader, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Hdr:
                    return await NntpCmdHdr.DispatchAsync(session, _articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Over:
                    return await NntpCmdOver.DispatchAsync(session, _articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Next:
                    return await NntpCmdNext.DispatchAsync(session, _articleStorage, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Last:
                    return await NntpCmdLast.DispatchAsync(session, _articleStorage, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Check:
                    return await NntpCmdCheck.DispatchAsync(
                        session,
                        _historyDatabase,
                        line,
                        cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Ihave:
                    return await NntpCmdIHave.DispatchAsync(
                        session,
                        _historyDatabase,
                        _transitStorage,
                        line,
                        session.LineReader,
                        cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Takethis:
                    return await NntpCmdTakethis.DispatchAsync(
                        session,
                        _historyDatabase,
                        _transitStorage,
                        line,
                        session.LineReader,
                        cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.StartTls:
                    if (_tlsCertificateSource is null)
                    {
                        await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    _ = await NntpCmdStartTls.DispatchAsync(session, _tlsCertificateSource, cancellationToken).ConfigureAwait(false);
                    break;
                case NntpKnownVerb.Compress:
                    _ = await NntpCmdCompress.DispatchAsync(
                        session,
                        line,
                        ct => ActivateCompressionAsync(session, ct),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case NntpKnownVerb.Newgroups:
                case NntpKnownVerb.Newnews:
                case NntpKnownVerb.Slave:
                    await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                    break;
                case NntpKnownVerb.Unknown:
                    break;
                default:
                    if (!session.State.IsCompressionActive)
                    {
                        NntpCommandDispatcherLog.LogUnknownCommand(_logger, FormatLineForLog(lineBytes));
                    }

                    await session.Writer.WritePreencodedAsync(NntpPreencodedResponses.UnknownCommand500, cancellationToken).ConfigureAwait(false);
                    break;
            }

            return !session.State.QuitRequested;
        }

        /// <summary>
        /// Activates RFC 8054 DEFLATE compression on the session transport after a successful COMPRESS response.
        /// </summary>
        /// <param name="session">Session whose transport and line reader/writer are rebound after activation.</param>
        /// <param name="cancellationToken">Cancellation token for transport upgrade I/O.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when compression is active and I/O is rebound.</returns>
        /// <remarks>
        /// Invoked from <c>COMPRESS</c> handler success path. <see cref="NntpSession.RebindTransportIo"/> must run after the transport
        /// negotiates DEFLATE so subsequent commands read and write through the compressed stream.
        /// </remarks>
        private static async ValueTask ActivateCompressionAsync(NntpSession session, CancellationToken cancellationToken)
        {
            await session.Transport.ActivateDeflateCompressionAsync(cancellationToken).ConfigureAwait(false);
            session.RebindTransportIo();
        }

        /// <summary>
        /// Formats a command line for debug logging, redacting secrets and summarizing non-text payloads.
        /// </summary>
        /// <param name="lineBytes">Raw command line bytes without CRLF.</param>
        /// <returns>
        /// Redacted ASCII text, <c>&lt;empty&gt;</c> for zero-length input, or a length summary when the line
        /// exceeds 512 bytes or contains non-printable bytes.
        /// </returns>
        /// <remarks>
        /// The 512-byte cap matches typical NNTP line limits and prevents logging oversized or binary COMPRESS-era payloads.
        /// </remarks>
        private static string FormatLineForLog(ReadOnlyMemory<byte> lineBytes)
        {
            ReadOnlySpan<byte> span = lineBytes.Span;
            if (span.Length == 0)
            {
                return "<empty>";
            }

            return span.Length > 512 || !IsPrintableAscii(span)
                ? $"<non-text line {span.Length} bytes>"
                : RedactLine(Encoding.ASCII.GetString(span));
        }

        /// <summary>
        /// Determines whether every byte in a command line is safe to log as printable ASCII.
        /// </summary>
        /// <param name="span">Candidate command bytes.</param>
        /// <returns>
        /// <see langword="true"/> when each byte is tab or in the printable ASCII range (<c>0x20</c>–<c>0x7E</c>);
        /// otherwise <see langword="false"/>.
        /// </returns>
        private static bool IsPrintableAscii(ReadOnlySpan<byte> span)
        {
            foreach (byte value in span)
            {
                if (value is (byte)'\t' or (>= 0x20 and <= 0x7E))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Redacts AUTHINFO and PASS substrings from a decoded command line before logging.
        /// </summary>
        /// <param name="line">Decoded ASCII command line.</param>
        /// <returns>Original <paramref name="line"/> or <c>AUTHINFO &lt;redacted&gt;</c> when credentials may be present.</returns>
        /// <remarks>
        /// Any line containing <c>PASS</c> or <c>AUTHINFO</c> (case-insensitive) is collapsed to a single redacted label so
        /// passwords and SASL payloads never appear in debug logs.
        /// </remarks>
        private static string RedactLine(string line)
        {
            return line.Contains("PASS", StringComparison.OrdinalIgnoreCase)
                || line.Contains("AUTHINFO", StringComparison.OrdinalIgnoreCase)
                ? "AUTHINFO <redacted>"
                : line;
        }
    }
}
