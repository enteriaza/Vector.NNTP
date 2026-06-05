// <copyright file="NntpCommandDispatcher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: routes classified verbs to per-command handlers.

using Microsoft.Extensions.Logging;
using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;
using Vector.NNTP.Sockets.Tls;
using Vector.NNTP.Sockets.Transport.Commands;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Dispatches classified NNTP verbs to command handlers after gate checks.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpCommandDispatcher"/> class.
    /// </remarks>
    /// <param name="auth">Authentication service.</param>
    /// <param name="articleStorage">Optional reader storage.</param>
    /// <param name="transitStorage">Optional transit storage.</param>
    /// <param name="historyDatabase">Optional transit history database for CHECK and TAKETHIS/IHAVE record.</param>
    /// <param name="tlsCertificateSource">Optional TLS certificate source.</param>
    /// <param name="scramCredentialStore">Optional SCRAM credential store for capability advertisement.</param>
    /// <param name="logger">Logger.</param>
    public sealed partial class NntpCommandDispatcher(
        NntpAuthenticationService auth,
        INntpArticleStorage? articleStorage,
        INntpTransitStorage? transitStorage,
        IHistoryDatabase? historyDatabase,
        ITlsCertificateSource? tlsCertificateSource,
        IScramCredentialStore? scramCredentialStore,
        ILogger<NntpCommandDispatcher> logger)
    {
        private readonly NntpAuthenticationService _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        private readonly INntpArticleStorage? _articleStorage = articleStorage;
        private readonly INntpTransitStorage? _transitStorage = transitStorage;
        private readonly IHistoryDatabase? _historyDatabase = historyDatabase;
        private readonly ITlsCertificateSource? _tlsCertificateSource = tlsCertificateSource;
        private readonly IScramCredentialStore? _scramCredentialStore = scramCredentialStore;
        private readonly ILogger<NntpCommandDispatcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Dispatches a command line to the appropriate handler.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="line">Command line without CRLF.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when the session should continue; false when quitting.</returns>
        internal async ValueTask<bool> DispatchAsync(
            NntpSession session,
            string line,
            CancellationToken cancellationToken)
        {
            return await DispatchBytesAsync(session, Encoding.ASCII.GetBytes(line), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispatches a command line to the appropriate handler.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="lineBytes">Command line bytes without CRLF.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> when the session should continue; false when quitting.</returns>
        internal async ValueTask<bool> DispatchBytesAsync(
            NntpSession session,
            ReadOnlyMemory<byte> lineBytes,
            CancellationToken cancellationToken)
        {
            string line = Encoding.ASCII.GetString(lineBytes.Span);
            if (!session.State.MultiLineBodyPending && !session.State.IsCompressionActive)
            {
                LogCommandReceived(FormatLineForLog(lineBytes));
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
                    LogUnknownCommand(FormatLineForLog(lineBytes));
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
                        this._historyDatabase,
                        this._transitStorage,
                        NntpCommandLineHelpers.GetVerb(line),
                        line,
                        session.LineReader,
                        cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Takethis:
                    return await NntpCmdTakethis.DispatchAsync(
                        session,
                        this._historyDatabase,
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
                        LogUnknownCommand(FormatLineForLog(lineBytes));
                    }

                    await session.Writer.WritePreencodedAsync(NntpPreencodedResponses.UnknownCommand500, cancellationToken).ConfigureAwait(false);
                    break;
            }

            return !session.State.QuitRequested;
        }

        /// <summary>
        /// Activates compression on the session transport.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the compression is activated.</returns>
        private static async ValueTask ActivateCompressionAsync(NntpSession session, CancellationToken cancellationToken)
        {
            await session.Transport.ActivateDeflateCompressionAsync(cancellationToken).ConfigureAwait(false);
            session.RebindTransportIo();
        }

        /// <summary>
        /// Formats a command line for logging, redacting secrets and replacing non-text payloads with a summary.
        /// </summary>
        /// <param name="lineBytes">Command line bytes without CRLF.</param>
        /// <returns>Safe log text.</returns>
        private static string FormatLineForLog(ReadOnlyMemory<byte> lineBytes)
        {
            ReadOnlySpan<byte> span = lineBytes.Span;
            if (span.Length == 0)
            {
                return "<empty>";
            }

            if (span.Length > 512 || !IsPrintableAscii(span))
            {
                return $"<non-text line {span.Length} bytes>";
            }

            return RedactLine(Encoding.ASCII.GetString(span));
        }

        /// <summary>
        /// Determines whether a span is safe to render as an ASCII command line in logs.
        /// </summary>
        /// <param name="span">Candidate command bytes.</param>
        /// <returns><see langword="true"/> when every byte is tab or printable ASCII.</returns>
        private static bool IsPrintableAscii(ReadOnlySpan<byte> span)
        {
            foreach (byte value in span)
            {
                if (value is (byte)'\t' or >= 0x20 and <= 0x7E)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static string RedactLine(string line)
        {
            return line.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("AUTHINFO", StringComparison.OrdinalIgnoreCase)
                ? "AUTHINFO <redacted>"
                : line;
        }
    }
}
