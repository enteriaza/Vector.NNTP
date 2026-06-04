// <copyright file="NntpSessionRunner.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: per-connection command loop over PipeReader/PipeWriter.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vector.NNTP.Session.Coordination;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Sockets.Metrics;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Tls;

namespace Vector.NNTP.Sockets.Transport
{
    /// <summary>
    /// Runs the NNTP command loop for one accepted connection.
    /// </summary>
    /// <remarks>
    /// Owns connection session registration, distributed admission release, and teardown for every exit path.
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpSessionRunner"/> class.
    /// </remarks>
    /// <param name="dispatcher">Command dispatcher.</param>
    /// <param name="profile">Host profile.</param>
    /// <param name="options">Server options.</param>
    /// <param name="sessionDatabase">Node-local session registry.</param>
    /// <param name="sessionCoordinator">Distributed admission coordinator.</param>
    /// <param name="transitPeerCoordinator">Transit peer Redis admission coordinator.</param>
    /// <param name="quotaEnforcer">Quota enforcement service.</param>
    /// <param name="tlsCertificateSource">Optional TLS certificate source.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public sealed class NntpSessionRunner(
        NntpCommandDispatcher dispatcher,
        INntpHostProfile profile,
        IOptions<NntpServerOptions> options,
        ISessionDatabase sessionDatabase,
        INntpSessionCoordinator sessionCoordinator,
        INntpTransitPeerCoordinator transitPeerCoordinator,
        NntpQuotaEnforcer quotaEnforcer,
        ITlsCertificateSource? tlsCertificateSource,
        ILogger<NntpSessionRunner> logger,
        ILoggerFactory? loggerFactory = null)
    {
        private readonly NntpCommandDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        private readonly INntpHostProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        private readonly IOptions<NntpServerOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));
        private readonly INntpSessionCoordinator _sessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
        private readonly INntpTransitPeerCoordinator _transitPeerCoordinator =
            transitPeerCoordinator ?? throw new ArgumentNullException(nameof(transitPeerCoordinator));
        private readonly NntpQuotaEnforcer _quotaEnforcer = quotaEnforcer ?? throw new ArgumentNullException(nameof(quotaEnforcer));
        private readonly ITlsCertificateSource? _tlsCertificateSource = tlsCertificateSource;
        private readonly ILoggerFactory? _loggerFactory = loggerFactory;
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
            string configVersion = _options.Value.ServerIdentification;
            SessionContext connectionSession = new(
                context.SessionId,
                context.ClientRemoteEndPoint.Address,
                DateTimeOffset.UtcNow,
                configVersion,
                context.NodeName,
                context.TransitPeerId);
            if (!_sessionDatabase.TryAdd(connectionSession))
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                return;
            }

            NntpSessionState state = new()
            {
                IsTlsActive = tlsAlreadyActive,
                StartTlsCompleted = tlsAlreadyActive,
            };
            NntpSession session = new(context, state, _profile, _options, transport, _tlsCertificateSource, _loggerFactory);
            if (session.IsTrustedTransitPeer)
            {
                state.Mode = NntpSessionMode.Stream;
            }
            long rxBefore = context.RxBytes;
            long txBefore = context.TxBytes;

            await NntpSessionGreeting.SendAsync(session, cancellationToken).ConfigureAwait(false);

            using CancellationTokenSource readIdleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            string teardownReason = "disconnect";

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    readIdleCts.CancelAfter(_options.Value.IdleTimeout);
                    NntpByteLineReadResult read = await session.LineReader.ReadLineBytesAsync(readIdleCts.Token).ConfigureAwait(false);
                    if (read.IsCompleted)
                    {
                        teardownReason = "disconnect";
                        break;
                    }

                    if (read.Line.IsEmpty)
                    {
                        continue;
                    }

                    long rxBeforeCommand = context.RxBytes;
                    bool cont = await _dispatcher.DispatchBytesAsync(session, read.Line, cancellationToken).ConfigureAwait(false);
                    long commandBytes = Math.Max(0, context.RxBytes - rxBeforeCommand) + Math.Max(0, context.TxBytes - txBefore);
                    txBefore = context.TxBytes;

                    if (context.IsAuthenticated && context.Policy is not null)
                    {
                        QuotaEnforcementResult quotaResult = await _quotaEnforcer.ApplyBlockQuotaAfterCommandAsync(
                            context.Policy,
                            context.SessionId,
                            commandBytes,
                            cancellationToken).ConfigureAwait(false);
                        if (quotaResult.ShouldDeauthorize)
                        {
                            await TeardownAdmissionAsync(
                                context.Policy,
                                context.SessionId,
                                context.ClientRemoteEndPoint.Address.ToString(),
                                context.NodeName,
                                cancellationToken).ConfigureAwait(false);
                            context.ClearAuthentication();
                            if (_sessionDatabase.TryGet(context.SessionId, out SessionContext? row))
                            {
                                _ = row.TryDeauthorize();
                            }

                            state.DynamicSendLimiter = null;
                        }
                        else
                        {
                            long perSessionRate = await _quotaEnforcer.RefreshRateLimitAsync(context.Policy, cancellationToken).ConfigureAwait(false);
                            state.DynamicSendLimiter?.UpdateMaxSendBytesPerSecond(perSessionRate);
                        }
                    }

                    if (!cont)
                    {
                        teardownReason = "quit";
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || readIdleCts.Token.IsCancellationRequested)
            {
                teardownReason = cancellationToken.IsCancellationRequested ? "shutdown" : "idle_timeout";
            }
            catch (Exception)
            {
                teardownReason = "fault";
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
                long rxDelta = context.RxBytes - rxBefore;
                long txDelta = context.TxBytes - txBefore;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (_sessionDatabase.TryGet(context.SessionId, out SessionContext? accountingRow))
                {
                    accountingRow.AddRxBytes(rxDelta, now);
                    accountingRow.AddTxBytes(txDelta, now);
                }

                await TeardownConnectionAsync(context, transport, teardownReason, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Teardown the connection.
        /// </summary>
        /// <param name="context">Connection context.</param>
        /// <param name="transport">Transport.</param>
        /// <param name="reason">Reason for teardown.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the connection is teared down.</returns>
        private async Task TeardownConnectionAsync(
            NntpConnectionContext context,
            INntpSessionTransport transport,
            string reason,
            CancellationToken cancellationToken)
        {
            try
            {
                if (context.AdmissionAcquired && context.Policy is not null)
                {
                    await TeardownAdmissionAsync(
                        context.Policy,
                        context.SessionId,
                        context.ClientRemoteEndPoint.Address.ToString(),
                        context.NodeName,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (_sessionDatabase.TryGet(context.SessionId, out SessionContext? row) &&
                         row.SessionPolicy is NntpSessionPolicy boundPolicy &&
                         boundPolicy.RequiresDistributedAdmission())
                {
                    await TeardownAdmissionAsync(
                        boundPolicy,
                        context.SessionId,
                        context.ClientRemoteEndPoint.Address.ToString(),
                        context.NodeName,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await ReleaseTransitPeerResourcesAsync(context, cancellationToken).ConfigureAwait(false);
                _ = _sessionDatabase.TryRemove(context.SessionId, out _);
                await transport.DisposeAsync().ConfigureAwait(false);
                NntpSessionRunnerLog.SessionTeardown(_logger, context.SessionId, reason);
            }
        }

        private async ValueTask ReleaseTransitPeerResourcesAsync(
            NntpConnectionContext context,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(context.TransitPeerId))
            {
                return;
            }

            try
            {
                NntpTransitPeerMetrics.RecordActiveConnection(context.TransitPeerId, -1);
            }
            catch (Exception ex)
            {
                NntpSessionRunnerLog.TransitPeerMetricsFailed(_logger, ex, context.SessionId);
            }

            try
            {
                await _transitPeerCoordinator
                    .ReleaseAsync(context.TransitPeerId, context.SessionId, context.NodeName, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                NntpTransitPeerMetrics.RecordRedisError(context.TransitPeerId);
                NntpSessionRunnerLog.TransitPeerReleaseFailed(_logger, ex, context.SessionId, context.TransitPeerId);
            }
        }

        /// <summary>
        /// Teardown the admission.
        /// </summary>
        /// <param name="policy">Session policy.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="clientIpText">Client IP text.</param>
        /// <param name="nodeName">Stable cluster node identity.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the admission is teared down.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the policy is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the session identifier or client IP text is null or empty.</exception>
        /// <exception cref="Exception">Thrown when an error occurs while releasing the admission.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the cancellation token is cancelled.</exception>
        private async Task TeardownAdmissionAsync(
            NntpSessionPolicy policy,
            string sessionId,
            string clientIpText,
            string nodeName,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentException.ThrowIfNullOrEmpty(clientIpText);
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            try
            {
                await _sessionCoordinator.ReleaseAsync(
                    policy,
                    sessionId,
                    clientIpText,
                    nodeName,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                NntpSessionRunnerLog.AdmissionReleaseFailed(_logger, ex, sessionId);
            }
        }
    }
}
