// <copyright file="NntpSessionRunner.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: per-connection command loop over PipeReader/PipeWriter.

namespace Vector.NNTP.Sockets.Transport
{
    using Authentication;
    using Configuration;
    using HostProfile;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Responses;
    using Session;
    using Tls;

    /// <summary>
    /// Runs the NNTP command loop for one accepted connection.
    /// </summary>
    public sealed class NntpSessionRunner
    {
        private readonly NntpCommandDispatcher _dispatcher;
        private readonly INntpHostProfile _profile;
        private readonly IOptions<NntpServerOptions> _options;
        private readonly ITlsCertificateSource? _tlsCertificateSource;
        private readonly INntpSessionAdmissionTracker? _admissionTracker;
        private readonly ILogger<NntpSessionRunner> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpSessionRunner"/> class.
        /// </summary>
        /// <param name="dispatcher">Command dispatcher.</param>
        /// <param name="profile">Host profile.</param>
        /// <param name="options">Server options.</param>
        /// <param name="tlsCertificateSource">Optional TLS certificate source.</param>
        /// <param name="admissionTracker">Optional session admission tracker for limit enforcement.</param>
        /// <param name="logger">Logger.</param>
        public NntpSessionRunner(
            NntpCommandDispatcher dispatcher,
            INntpHostProfile profile,
            IOptions<NntpServerOptions> options,
            ITlsCertificateSource? tlsCertificateSource,
            INntpSessionAdmissionTracker? admissionTracker,
            ILogger<NntpSessionRunner> logger)
        {
            this._dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this._profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this._options = options ?? throw new ArgumentNullException(nameof(options));
            this._tlsCertificateSource = tlsCertificateSource;
            this._admissionTracker = admissionTracker;
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Runs a session over the given transport until QUIT or disconnect.
        /// </summary>
        /// <param name="transport">Socket transport (cleartext or pre-authenticated TLS).</param>
        /// <param name="context">Connection context.</param>
        /// <param name="tlsAlreadyActive">Whether TLS was negotiated before the greeting.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="Task"/> that completes when the session ends.</returns>
        public async Task RunAsync(
            INntpSessionTransport transport,
            NntpConnectionContext context,
            bool tlsAlreadyActive,
            CancellationToken cancellationToken)
        {
            var state = new NntpSessionState
            {
                IsTlsActive = tlsAlreadyActive,
                StartTlsCompleted = tlsAlreadyActive,
            };
            var session = new NntpSession(context, state, this._profile, this._options, transport, this._tlsCertificateSource);

            await NntpSessionGreeting.SendAsync(session, cancellationToken).ConfigureAwait(false);

            using var readIdleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    readIdleCts.CancelAfter(this._options.Value.IdleTimeout);
                    string? line = await session.LineReader.ReadLineAsync(readIdleCts.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    bool cont = await this._dispatcher.DispatchAsync(session, line, cancellationToken).ConfigureAwait(false);
                    if (!cont)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || readIdleCts.Token.IsCancellationRequested)
            {
                // shutdown or idle timeout
            }
            catch (Exception)
            {
                try
                {
                    await session.Writer.WritePreencodedAsync(NntpPreencodedResponses.ProgramFault503, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort 503; connection may already be closed.
                }
            }
            finally
            {
                if (this._admissionTracker is not null &&
                    context.IsAuthenticated &&
                    context.Policy is not null)
                {
                    this._admissionTracker.Leave(context.Policy, context.ClientRemoteEndPoint.Address);
                }

                await transport.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
