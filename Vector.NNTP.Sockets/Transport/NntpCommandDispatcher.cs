// <copyright file="NntpCommandDispatcher.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: routes classified verbs to per-command handlers.

namespace Vector.NNTP.Sockets.Transport
{
    using Authentication;
    using Microsoft.Extensions.Logging;
    using Vector.NNTP.Sockets.Transport.Commands;
    using Responses;
    using Session;
    using Storage;
    using Tls;

    /// <summary>
    /// Dispatches classified NNTP verbs to command handlers after gate checks.
    /// </summary>
    public sealed partial class NntpCommandDispatcher
    {
        private readonly NntpAuthenticationService _auth;
        private readonly INntpArticleStorage? _articleStorage;
        private readonly INntpTransitStorage? _transitStorage;
        private readonly ITlsCertificateSource? _tlsCertificateSource;
        private readonly IScramCredentialStore? _scramCredentialStore;
        private readonly ILogger<NntpCommandDispatcher> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpCommandDispatcher"/> class.
        /// </summary>
        /// <param name="auth">Authentication service.</param>
        /// <param name="articleStorage">Optional reader storage.</param>
        /// <param name="transitStorage">Optional transit storage.</param>
        /// <param name="tlsCertificateSource">Optional TLS certificate source.</param>
        /// <param name="scramCredentialStore">Optional SCRAM credential store for capability advertisement.</param>
        /// <param name="logger">Logger.</param>
        public NntpCommandDispatcher(
            NntpAuthenticationService auth,
            INntpArticleStorage? articleStorage,
            INntpTransitStorage? transitStorage,
            ITlsCertificateSource? tlsCertificateSource,
            IScramCredentialStore? scramCredentialStore,
            ILogger<NntpCommandDispatcher> logger)
        {
            this._auth = auth ?? throw new ArgumentNullException(nameof(auth));
            this._articleStorage = articleStorage;
            this._transitStorage = transitStorage;
            this._tlsCertificateSource = tlsCertificateSource;
            this._scramCredentialStore = scramCredentialStore;
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

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
                this.LogUnknownCommand(RedactLine(line));
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
                await this._auth.HandleSaslContinuationAsync(session, line.Trim(), cancellationToken).ConfigureAwait(false);
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
                    return await NntpCapabilities.DispatchAsync(session, this._scramCredentialStore, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Mode:
                    return await NntpCmdMode.DispatchAsync(session, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Authinfo:
                    await NntpCmdAuthinfo.DispatchAsync(session, this._auth, line, cancellationToken).ConfigureAwait(false);
                    break;
                case NntpKnownVerb.Group:
                    return await NntpCmdGroup.DispatchAsync(session, this._articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.List:
                    return await NntpCmdList.DispatchAsync(session, this._articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.ListGroup:
                    return await NntpCmdListGroup.DispatchAsync(session, this._articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Article:
                    return await NntpCmdArticle.DispatchAsync(
                        session,
                        this._articleStorage,
                        NntpCommandLineHelpers.GetVerb(line),
                        line,
                        cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Post:
                    return await NntpCmdPost.DispatchAsync(session, this._articleStorage, session.LineReader, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Hdr:
                    return await NntpCmdHdr.DispatchAsync(session, this._articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Over:
                    return await NntpCmdOver.DispatchAsync(session, this._articleStorage, line, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Next:
                    return await NntpCmdNext.DispatchAsync(session, this._articleStorage, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Last:
                    return await NntpCmdLast.DispatchAsync(session, this._articleStorage, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Check:
                    return await NntpCmdCheck.DispatchAsync(
                        session,
                        this._transitStorage,
                        NntpCommandLineHelpers.GetVerb(line),
                        line,
                        session.LineReader,
                        cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.Takethis:
                    return await NntpCmdTakethis.DispatchAsync(session, this._transitStorage, line, session.LineReader, cancellationToken).ConfigureAwait(false);
                case NntpKnownVerb.StartTls:
                    if (this._tlsCertificateSource is null)
                    {
                        await NntpReaderErrors.WriteServiceUnavailable503(session, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await NntpCmdStartTls.DispatchAsync(session, this._tlsCertificateSource, cancellationToken).ConfigureAwait(false);
                    break;
                case NntpKnownVerb.Compress:
                    await NntpCmdCompress.DispatchAsync(
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
                default:
                    this.LogUnknownCommand(RedactLine(line));
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
            if (line.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("AUTHINFO", StringComparison.OrdinalIgnoreCase))
            {
                return "AUTHINFO <redacted>";
            }

            return line;
        }
    }
}
