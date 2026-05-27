// <copyright file="NntpSessionRunner.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: per-connection command loop over PipeReader/PipeWriter.

using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.HostProfile;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Tls;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Runs the NNTP command loop for one accepted connection.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpSessionRunner"/> class.
    /// </remarks>
    /// <param name="dispatcher">Command dispatcher.</param>
    /// <param name="profile">Host profile.</param>
    /// <param name="options">Server options.</param>
    /// <param name="tlsCertificateSource">Optional TLS certificate source.</param>
    /// <param name="admissionTracker">Optional session admission tracker for limit enforcement.</param>
    /// <param name="logger">Logger.</param>
    public sealed class NntpSessionRunner(
        NntpCommandDispatcher dispatcher,
        INntpHostProfile profile,
        IOptions<NntpServerOptions> options,
        ITlsCertificateSource? tlsCertificateSource,
        INntpSessionAdmissionTracker? admissionTracker,
        ILogger<NntpSessionRunner> logger)
    {
        private readonly NntpCommandDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        private readonly INntpHostProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        private readonly IOptions<NntpServerOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
        private readonly ITlsCertificateSource? _tlsCertificateSource = tlsCertificateSource;
        private readonly INntpSessionAdmissionTracker? _admissionTracker = admissionTracker;
        private readonly ILogger<NntpSessionRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
            NntpSessionState state = new()
            {
                IsTlsActive = tlsAlreadyActive,
                StartTlsCompleted = tlsAlreadyActive,
            };
            NntpSession session = new(context, state, _profile, _options, transport, _tlsCertificateSource);

            await NntpSessionGreeting.SendAsync(session, cancellationToken).ConfigureAwait(false);

            using CancellationTokenSource readIdleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    readIdleCts.CancelAfter(_options.Value.IdleTimeout);
                    string? line = await session.LineReader.ReadLineAsync(readIdleCts.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    bool cont = await _dispatcher.DispatchAsync(session, line, cancellationToken).ConfigureAwait(false);
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
                if (_admissionTracker is not null &&
                    context.IsAuthenticated &&
                    context.Policy is not null)
                {
                    _admissionTracker.Leave(context.Policy, context.ClientRemoteEndPoint.Address);
                }

                await transport.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
