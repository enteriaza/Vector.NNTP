// <copyright file="NntpCommandDispatcher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: routes classified verbs to per-command handlers.

using Vector.NNTP.Sockets.Authentication;
using Microsoft.Extensions.Logging;
using Vector.NNTP.Sockets.Transport.Commands;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Storage;
using Vector.NNTP.Sockets.Tls;

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
    /// <param name="tlsCertificateSource">Optional TLS certificate source.</param>
    /// <param name="scramCredentialStore">Optional SCRAM credential store for capability advertisement.</param>
    /// <param name="logger">Logger.</param>
    public sealed partial class NntpCommandDispatcher(
        NntpAuthenticationService auth,
        INntpArticleStorage? articleStorage,
        INntpTransitStorage? transitStorage,
        ITlsCertificateSource? tlsCertificateSource,
        IScramCredentialStore? scramCredentialStore,
        ILogger<NntpCommandDispatcher> logger)
    {
        private readonly NntpAuthenticationService _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        private readonly INntpArticleStorage? _articleStorage = articleStorage;
        private readonly INntpTransitStorage? _transitStorage = transitStorage;
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
            NntpKnownVerb verb = NntpCommandVerb.Classify(line.AsSpan());
            if (verb == NntpKnownVerb.Unknown)
            {
                LogUnknownCommand(RedactLine(line));
                await session.Writer.WritePreencodedAsync(NntpPreencodedResponses.UnknownCommand500, cancellationToken).ConfigureAwait(false);
                return !session.State.QuitRequested;
            }

            NntpGateResult gate = NntpCommandGate.Evaluate(session, verb);
            if (gate != NntpGateResult.Allow)
            {
                await NntpCommandGate.WriteRejectionAsync(session, gate, cancellationToken).ConfigureAwait(false);
                return !session.State.QuitRequested;
            }

            if (session.State.AuthenticationState == AuthenticationState.SaslInProgress &&
                !line.StartsWith("AUTHINFO", StringComparison.OrdinalIgnoreCase))
            {
                await _auth.HandleSaslContinuationAsync(session, line.Trim(), cancellationToken).ConfigureAwait(false);
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
                        _transitStorage,
                        NntpCommandLineHelpers.GetVerb(line),
                        line,
                        session.LineReader,
                        cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Takethis:
                    return await NntpCmdTakethis.DispatchAsync(session, _transitStorage, line, session.LineReader, cancellationToken).ConfigureAwait(false);
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
                case NntpKnownVerb.Newnews:
                    return await NntpCmdNewnews.DispatchAsync(session, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Newgroups:
                    return await NntpCmdNewgroups.DispatchAsync(session, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Slave:
                    return await NntpCmdSlave.DispatchAsync(session, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Unknown:
                    break;
                default:
                    LogUnknownCommand(RedactLine(line));
                    await session.Writer.WritePreencodedAsync(NntpPreencodedResponses.UnknownCommand500, cancellationToken).ConfigureAwait(false);
                    break;
            }

            return !session.State.QuitRequested;
        }

        private static async ValueTask ActivateCompressionAsync(NntpSession session, CancellationToken cancellationToken)
        {
            await session.Transport.ActivateDeflateCompressionAsync(cancellationToken).ConfigureAwait(false);
            session.RebindTransportIo();
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
