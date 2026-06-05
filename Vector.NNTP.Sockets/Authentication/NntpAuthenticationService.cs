// <copyright file="NntpAuthenticationService.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: AUTHINFO USER/PASS and SASL mechanism orchestration.

using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Authentication.Sasl;
using Vector.NNTP.Sockets.Responses;
using Vector.NNTP.Sockets.Session;
using Vector.NNTP.Sockets.Transport;

namespace Vector.NNTP.Sockets.Authentication
{
    /// <summary>
    /// Handles AUTHINFO USER/PASS and SASL PLAIN, LOGIN, SCRAM, and CRAM-MD5 on the NNTP wire.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NntpAuthenticationService"/> class.
    /// </remarks>
    /// <param name="validator">Password validator for USER/PASS, PLAIN, and LOGIN.</param>
    /// <param name="sessionCoordinator">Distributed session admission coordinator.</param>
    /// <param name="sessionDatabase">Node-local session registry.</param>
    /// <param name="blockQuotaCoordinator">Block quota initializer for byte-limited accounts.</param>
    /// <param name="rateAllocationCoordinator">Fair-share rate allocation coordinator.</param>
    /// <param name="idleOptions">Idle timeout options for Redis lease TTL sizing.</param>
    /// <param name="scramStore">Optional SCRAM credential store.</param>
    /// <param name="cramStore">Optional CRAM-MD5 secret store.</param>
    /// <param name="saslAccountAuthenticator">Optional SASL completion handler for policy lookup.</param>
    public sealed class NntpAuthenticationService(
        INntpCredentialValidator validator,
        INntpSessionCoordinator sessionCoordinator,
        ISessionDatabase sessionDatabase,
        INntpBlockQuotaCoordinator blockQuotaCoordinator,
        INntpRateAllocationCoordinator rateAllocationCoordinator,
        IOptionsMonitor<NntpSessionIdleOptions> idleOptions,
        IScramCredentialStore? scramStore = null,
        ICramMd5CredentialStore? cramStore = null,
        INntpSaslAccountAuthenticator? saslAccountAuthenticator = null)
    {
        private readonly INntpCredentialValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        private readonly INntpSessionCoordinator _sessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
        private readonly ISessionDatabase _sessionDatabase = sessionDatabase ?? throw new ArgumentNullException(nameof(sessionDatabase));
        private readonly INntpBlockQuotaCoordinator _blockQuotaCoordinator = blockQuotaCoordinator ?? throw new ArgumentNullException(nameof(blockQuotaCoordinator));
        private readonly INntpRateAllocationCoordinator _rateAllocationCoordinator = rateAllocationCoordinator ?? throw new ArgumentNullException(nameof(rateAllocationCoordinator));
        private readonly IOptionsMonitor<NntpSessionIdleOptions> _idleOptions = idleOptions ?? throw new ArgumentNullException(nameof(idleOptions));
        private readonly IScramCredentialStore? _scramStore = scramStore;
        private readonly ICramMd5CredentialStore? _cramStore = cramStore;
        private readonly INntpSaslAccountAuthenticator? _saslAccountAuthenticator = saslAccountAuthenticator;

        /// <summary>
        /// Handles an AUTHINFO command line.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        public async ValueTask HandleAuthInfoAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            if (!session.IsAuthInfoPermitted)
            {
                await session.Writer.WriteLineAsync("483 Encryption required", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (session.Connection.IsAuthenticated)
            {
                await session.Writer.WriteLineAsync("502 Already authenticated", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (ContainsToken(line, "USER"))
            {
                await HandleUserAsync(session, line, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (ContainsToken(line, "PASS"))
            {
                await HandlePassAsync(session, line, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (ContainsToken(line, "SASL"))
            {
                await HandleSaslAsync(session, line, cancellationToken).ConfigureAwait(false);
                return;
            }

            await session.Writer.WriteLineAsync("501 AUTHINFO command not recognized", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a SASL continuation line (383 response payload).
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="payload">Base64 or plain continuation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when handled.</returns>
        public async ValueTask HandleSaslContinuationAsync(NntpSession session, string payload, CancellationToken cancellationToken)
        {
            if (session.State.AuthenticationState != Session.AuthenticationState.SaslInProgress)
            {
                await session.Writer.WriteLineAsync("503 No SASL exchange in progress", cancellationToken).ConfigureAwait(false);
                return;
            }

            string mech = session.State.PendingSaslMechanism ?? string.Empty;
            if (payload == "*")
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("481 Authentication cancelled", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.Equals("LOGIN", StringComparison.OrdinalIgnoreCase))
            {
                await HandleLoginContinuationAsync(session, payload, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.Equals("CRAM-MD5", StringComparison.OrdinalIgnoreCase) && session.State.SaslServerState is string challenge)
            {
                await HandleCramResponseAsync(session, payload, challenge, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.StartsWith("SCRAM-", StringComparison.OrdinalIgnoreCase) && session.State.SaslServerState is ScramSaslState scramState)
            {
                await HandleScramFinishAsync(session, scramState, payload, cancellationToken).ConfigureAwait(false);
                return;
            }

            ResetAuth(session);
            await session.Writer.WriteLineAsync("503 SASL continuation not supported", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a USER command line.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask HandleUserAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            string? user = ExtractLastToken(line.AsSpan());
            if (string.IsNullOrEmpty(user))
            {
                await session.Writer.WriteLineAsync("501 USER requires argument", cancellationToken).ConfigureAwait(false);
                return;
            }

            session.State.PendingAuthInfoUser = user;
            session.State.AuthenticationState = Session.AuthenticationState.AuthInfoUserPending;
            TryBeginSessionAuthenticating(session, AuthenticatingPhase.SaslContinuation);
            await session.Writer.WriteLineAsync("381 Password required", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a PASS command line.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask HandlePassAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            if (session.State.AuthenticationState != Session.AuthenticationState.AuthInfoUserPending)
            {
                await session.Writer.WriteLineAsync("503 AUTHINFO USER required first", cancellationToken).ConfigureAwait(false);
                return;
            }

            string? pass = ExtractLastToken(line.AsSpan());
            string user = session.State.PendingAuthInfoUser ?? string.Empty;
            NntpAuthResult result = await _validator.ValidatePasswordAsync(
                NntpAuthMechanisms.AuthInfoUserPass,
                user,
                pass ?? string.Empty,
                session.Connection.ClientRemoteEndPoint.Address,
                session.State.IsTlsActive,
                cancellationToken).ConfigureAwait(false);
            await WriteAuthResultAsync(session, result, cancellationToken).ConfigureAwait(false);
            if (result.Status != NntpAuthStatus.Success)
            {
                session.State.PendingAuthInfoUser = null;
                session.State.AuthenticationState = Session.AuthenticationState.None;
            }
        }

        /// <summary>
        /// Handles a SASL command line.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="line">Full command line.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask HandleSaslAsync(NntpSession session, string line, CancellationToken cancellationToken)
        {
            string? mech = ExtractMechanism(line.AsSpan());
            if (string.IsNullOrEmpty(mech))
            {
                await session.Writer.WriteLineAsync("501 SASL mechanism required", cancellationToken).ConfigureAwait(false);
                return;
            }

            string? initial = ExtractInitialResponse(line.AsSpan());
            session.State.PendingSaslMechanism = mech;
            session.State.AuthenticationState = Session.AuthenticationState.SaslInProgress;
            TryBeginSessionAuthenticating(session, AuthenticatingPhase.SaslContinuation);

            if (mech.Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
            {
                await HandlePlainAsync(session, initial, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.Equals("LOGIN", StringComparison.OrdinalIgnoreCase))
            {
                await session.Writer.WriteLineAsync("334 VXNlcm5hbWU6", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.StartsWith("SCRAM-", StringComparison.OrdinalIgnoreCase))
            {
                await HandleScramStartAsync(session, mech, initial, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (mech.Equals("CRAM-MD5", StringComparison.OrdinalIgnoreCase))
            {
                string challenge = CramMd5Mechanism.CreateChallenge();
                session.State.SaslServerState = challenge;
                await session.Writer.WriteLineAsync($"334 {challenge}", cancellationToken).ConfigureAwait(false);
                return;
            }

            ResetAuth(session);
            await session.Writer.WriteLineAsync("503 Mechanism not supported", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a PLAIN SASL mechanism.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="initial">Initial response.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask HandlePlainAsync(NntpSession session, string? initial, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(initial))
            {
                await session.Writer.WriteLineAsync("383 Send credentials", cancellationToken).ConfigureAwait(false);
                return;
            }

            string decoded = DecodeMaybeBase64(initial);
            string[] parts = decoded.Split('\0');
            if (parts.Length < 3)
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            NntpAuthResult result = await _validator.ValidatePasswordAsync(
                NntpAuthMechanisms.SaslPlain,
                parts[1],
                parts[2],
                session.Connection.ClientRemoteEndPoint.Address,
                session.State.IsTlsActive,
                cancellationToken).ConfigureAwait(false);
            await WriteAuthResultAsync(session, result, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a LOGIN SASL mechanism continuation.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="payload">Base64 or plain continuation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask HandleLoginContinuationAsync(NntpSession session, string payload, CancellationToken cancellationToken)
        {
            string value = DecodeMaybeBase64(payload);
            if (session.State.SaslServerState is not LoginSaslState state)
            {
                state = new LoginSaslState(null);
            }

            if (string.IsNullOrEmpty(state.Username))
            {
                session.State.SaslServerState = new LoginSaslState(value);
                await session.Writer.WriteLineAsync("334 UGFzc3dvcmQ6", cancellationToken).ConfigureAwait(false);
                return;
            }

            NntpAuthResult result = await _validator.ValidatePasswordAsync(
                NntpAuthMechanisms.SaslLogin,
                state.Username,
                value,
                session.Connection.ClientRemoteEndPoint.Address,
                session.State.IsTlsActive,
                cancellationToken).ConfigureAwait(false);
            await WriteAuthResultAsync(session, result, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a SCRAM SASL mechanism continuation.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="scramState">SCRAM state.</param>
        /// <param name="payload">Base64 or plain continuation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask HandleScramFinishAsync(
            NntpSession session,
            ScramSaslState scramState,
            string payload,
            CancellationToken cancellationToken)
        {
            string? serverFinal = scramState.Mechanism.TryFinish(DecodeMaybeBase64(payload));
            if (serverFinal is null)
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            NntpSessionPolicy policy;
            if (_saslAccountAuthenticator is not null)
            {
                NntpAuthResult result = await _saslAccountAuthenticator.CompleteSaslAccountAsync(
                    NntpAuthMechanisms.SaslScramSha256,
                    scramState.Username,
                    session.Connection.ClientRemoteEndPoint.Address,
                    session.State.IsTlsActive,
                    cancellationToken).ConfigureAwait(false);

                if (result.Status != NntpAuthStatus.Success)
                {
                    await WriteAuthResultAsync(session, result, cancellationToken).ConfigureAwait(false);
                    return;
                }

                policy = result.Policy!;
            }
            else
            {
                policy = new NntpSessionPolicy(scramState.Username, allowPosting: true, NntpAccountType.RateLimited, string.Empty, 0, 0, 0, 0, string.Empty);
            }

            bool admitted = await CompleteAdmissionAndAuthenticateAsync(session, policy, cancellationToken).ConfigureAwait(false);
            ResetAuth(session);
            if (!admitted)
            {
                return;
            }

            await session.Writer.WriteLineAsync($"235 {serverFinal}", cancellationToken).ConfigureAwait(false);
            await ApplyPostAuthenticationEnforcementAsync(session, policy, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a SCRAM SASL mechanism start.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="mech">SCRAM mechanism.</param>
        /// <param name="initial">Initial response.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask HandleScramStartAsync(NntpSession session, string mech, string? initial, CancellationToken cancellationToken)
        {
            if (_scramStore is null || string.IsNullOrEmpty(initial))
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("503 SCRAM not available", cancellationToken).ConfigureAwait(false);
                return;
            }

            string clientFirst = DecodeMaybeBase64(initial);
            if (!ScramMechanismBegin.TryGetUsername(clientFirst, out string? username) ||
                !_scramStore.TryGetScramCredential(username, out ScramStoredCredential? cred))
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            (ScramMechanism state, string serverFirst) = ScramMechanism.Begin(mech, clientFirst, cred);
            session.State.SaslServerState = new ScramSaslState(username, state);
            await session.Writer.WriteLineAsync($"383 {serverFirst}", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a CRAM-MD5 SASL mechanism response.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="payload">Base64 or plain response.</param>
        /// <param name="challenge">Server challenge.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask HandleCramResponseAsync(NntpSession session, string payload, string challenge, CancellationToken cancellationToken)
        {
            if (_cramStore is null)
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("503 CRAM-MD5 not available", cancellationToken).ConfigureAwait(false);
                return;
            }

            string decoded = DecodeMaybeBase64(payload);
            int space = decoded.IndexOf(' ');
            if (space <= 0)
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            string user = decoded[..space];
            if (!_cramStore.TryGetCramSecret(user, out ReadOnlyMemory<byte> secret) ||
                !CramMd5Mechanism.Verify(user, decoded, challenge, secret.Span))
            {
                ResetAuth(session);
                await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_saslAccountAuthenticator is not null)
            {
                NntpAuthResult result = await _saslAccountAuthenticator.CompleteSaslAccountAsync(
                    NntpAuthMechanisms.SaslCramMd5,
                    user,
                    session.Connection.ClientRemoteEndPoint.Address,
                    session.State.IsTlsActive,
                    cancellationToken).ConfigureAwait(false);

                if (result.Status != NntpAuthStatus.Success)
                {
                    await WriteAuthResultAsync(session, result, cancellationToken).ConfigureAwait(false);
                    return;
                }

                NntpSessionPolicy policy = result.Policy!;
                bool admitted = await CompleteAdmissionAndAuthenticateAsync(session, policy, cancellationToken).ConfigureAwait(false);
                ResetAuth(session);
                if (!admitted)
                {
                    return;
                }

                await session.Writer.WriteLineAsync("235 Authentication succeeded", cancellationToken).ConfigureAwait(false);
                await ApplyPostAuthenticationEnforcementAsync(session, policy, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                NntpSessionPolicy devPolicy = new(user, allowPosting: true, NntpAccountType.RateLimited, string.Empty, 0, 0, 0, 0, string.Empty);
                bool admitted = await CompleteAdmissionAndAuthenticateAsync(session, devPolicy, cancellationToken).ConfigureAwait(false);
                ResetAuth(session);
                if (!admitted)
                {
                    return;
                }

                await session.Writer.WriteLineAsync("235 Authentication succeeded", cancellationToken).ConfigureAwait(false);
                await ApplyPostAuthenticationEnforcementAsync(session, devPolicy, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Writes the authentication result to the session.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="result">Authentication result.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask WriteAuthResultAsync(NntpSession session, NntpAuthResult result, CancellationToken cancellationToken)
        {
            switch (result.Status)
            {
                case NntpAuthStatus.Success:
                    NntpSessionPolicy policy = result.Policy!;
                    bool admitted = await CompleteAdmissionAndAuthenticateAsync(session, policy, cancellationToken).ConfigureAwait(false);
                    ResetAuth(session);
                    if (!admitted)
                    {
                        break;
                    }

                    await session.Writer.WriteLineAsync("281 Authentication accepted", cancellationToken).ConfigureAwait(false);
                    await ApplyPostAuthenticationEnforcementAsync(session, policy, cancellationToken).ConfigureAwait(false);
                    break;
                case NntpAuthStatus.TransientFailure:
                    RollbackSessionAuthenticating(session);
                    ResetAuth(session);
                    await session.Writer.WriteLineAsync("503 Temporary authentication failure", cancellationToken).ConfigureAwait(false);
                    break;
                case NntpAuthStatus.InvalidCredentials:
                    RollbackSessionAuthenticating(session);
                    ResetAuth(session);
                    await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    RollbackSessionAuthenticating(session);
                    ResetAuth(session);
                    await session.Writer.WriteLineAsync("481 Authentication failed", cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        /// <summary>
        /// Completes admission and authentication.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="policy">Session policy.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the admission is completed.</returns>
        private async ValueTask<bool> CompleteAdmissionAndAuthenticateAsync(
            NntpSession session,
            NntpSessionPolicy policy,
            CancellationToken cancellationToken)
        {
            string sessionId = session.Connection.SessionId;
            string clientIp = session.Connection.ClientRemoteEndPoint.Address.ToString();
            if (_sessionDatabase.TryGet(sessionId, out SessionContext? ctx))
            {
                _ = ctx.TryBindPendingAuthentication(policy.Username, policy.AccountKey, policy, AuthenticatingPhase.PendingAdmission);
            }

            int ttlSeconds = NntpSessionTtlCalculator.ComputeTtlSeconds(_idleOptions.CurrentValue.IdleTimeout);
            string nodeName = session.Connection.NodeName;
            NntpSessionAdmissionResult admit = await _sessionCoordinator.TryAdmitAsync(
                policy,
                sessionId,
                clientIp,
                nodeName,
                ttlSeconds,
                cancellationToken).ConfigureAwait(false);

            if (admit != NntpSessionAdmissionResult.Success)
            {
                RollbackSessionAuthenticating(session);
                await WriteAdmissionRejectedAsync(session, admit, cancellationToken).ConfigureAwait(false);
                return false;
            }

            if (_sessionDatabase.TryGet(sessionId, out SessionContext? row))
            {
                _ = row.TryCompleteAuthentication();
            }

            bool admissionAcquired = policy.RequiresDistributedAdmission();
            session.Connection.SetAuthenticated(policy, admissionAcquired);
            return true;
        }

        /// <summary>
        /// Applies post-authentication enforcement.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="policy">Session policy.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the enforcement is applied.</returns>
        private async ValueTask ApplyPostAuthenticationEnforcementAsync(NntpSession session, NntpSessionPolicy policy, CancellationToken cancellationToken)
        {
            if (policy.AccountType == NntpAccountType.ByteLimited && policy.ByteLimit > 0)
            {
                _ = await _blockQuotaCoordinator.TryInitializeQuotaAsync(policy.AccountKey, policy.ByteLimit, cancellationToken).ConfigureAwait(false);
            }

            if (policy.AccountType == NntpAccountType.RateLimited && policy.RateBytesPerSecond > 0 &&
                session.Transport is NntpSocketTransport socketTransport)
            {
                long perSession = await _rateAllocationCoordinator.GetPerSessionSendRateBytesPerSecondAsync(policy, cancellationToken).ConfigureAwait(false);
                DynamicSendRateLimitedStream? limiter = await socketTransport.ApplyOutboundRateLimitAsync(perSession, cancellationToken).ConfigureAwait(false);
                session.State.DynamicSendLimiter = limiter;
            }
        }

        /// <summary>
        /// Writes the admission rejected response to the session.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="result">Admission result.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the response is sent.</returns>
        private async ValueTask WriteAdmissionRejectedAsync(NntpSession session, NntpSessionAdmissionResult result, CancellationToken cancellationToken)
        {
            string line = result switch
            {
                NntpSessionAdmissionResult.MaxSessionsExceeded => NntpResponseLines.TooManySessions481,
                NntpSessionAdmissionResult.IpLimitExceeded => NntpResponseLines.TooManySourceAddresses481,
                NntpSessionAdmissionResult.BackendFailure => "503 Temporary authentication failure",
                NntpSessionAdmissionResult.Success => throw new NotImplementedException(),
                NntpSessionAdmissionResult.PolicyInvalid => throw new NotImplementedException(),
                _ => NntpResponseLines.AuthenticationFailed481,
            };
            ResetAuth(session);
            await session.Writer.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Tries to begin session authentication.
        /// </summary>
        /// <param name="session">Active session.</param>
        /// <param name="phase">Authentication phase.</param>
        private void TryBeginSessionAuthenticating(NntpSession session, AuthenticatingPhase phase)
        {
            if (_sessionDatabase.TryGet(session.Connection.SessionId, out SessionContext? ctx))
            {
                _ = ctx.TryBeginAuthenticating(phase);
            }
        }

        /// <summary>
        /// Rolls back session authentication.
        /// </summary>
        /// <param name="session">Active session.</param>
        private void RollbackSessionAuthenticating(NntpSession session)
        {
            if (_sessionDatabase.TryGet(session.Connection.SessionId, out SessionContext? ctx))
            {
                _ = ctx.TryRollbackAuthenticating();
            }
        }

        /// <summary>
        /// Resets session authentication state.
        /// </summary>
        /// <param name="session">Active session.</param>
        private void ResetAuth(NntpSession session)
        {
            this._saslAccountAuthenticator?.AbandonSaslExchange();
            session.State.AuthenticationState = Session.AuthenticationState.None;
            session.State.PendingAuthInfoUser = null;
            session.State.PendingSaslMechanism = null;
            session.State.SaslServerState = null;
        }

        /// <summary>
        /// Login SASL state.
        /// </summary>
        /// <param name="Username">Username.</param>
        private sealed record LoginSaslState(string? Username);

        /// <summary>
        /// SCRAM SASL state.
        /// </summary>
        /// <param name="Username">Username.</param>
        /// <param name="Mechanism">Mechanism.</param>
        private sealed record ScramSaslState(string Username, ScramMechanism Mechanism);

        /// <summary>
        /// Checks if a line contains a token.
        /// </summary>
        /// <param name="line">Line to check.</param>
        /// <param name="token">Token to check for.</param>
        /// <returns>True if the line contains the token, false otherwise.</returns>
        private static bool ContainsToken(string line, string token)
        {
            return line.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts the last token from a line.
        /// </summary>
        /// <param name="line">Line to extract the last token from.</param>
        /// <returns>The last token, or null if no token is found.</returns>
        private static string? ExtractLastToken(ReadOnlySpan<char> line)
        {
            int i = line.Length - 1;
            while (i >= 0 && char.IsWhiteSpace(line[i]))
            {
                i--;
            }

            int end = i;
            while (i >= 0 && !char.IsWhiteSpace(line[i]))
            {
                i--;
            }

            return i < end ? line[(i + 1)..(end + 1)].ToString() : null;
        }

        /// <summary>
        /// Extracts the mechanism from a line.
        /// </summary>
        /// <param name="line">Line to extract the mechanism from.</param>
        /// <returns>The mechanism, or null if no mechanism is found.</returns>
        private static string? ExtractMechanism(ReadOnlySpan<char> line)
        {
            int sasl = line.IndexOf("SASL".AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (sasl < 0)
            {
                return null;
            }

            ReadOnlySpan<char> rest = line[(sasl + 4)..].TrimStart();
            int space = rest.IndexOf(' ');
            return space < 0 ? rest.ToString() : rest[..space].ToString();
        }

        /// <summary>
        /// Extracts the initial response from a line.
        /// </summary>
        /// <param name="line">Line to extract the initial response from.</param>
        /// <returns>The initial response, or null if no initial response is found.</returns>
        private static string? ExtractInitialResponse(ReadOnlySpan<char> line)
        {
            int sasl = line.IndexOf("SASL".AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (sasl < 0)
            {
                return null;
            }

            ReadOnlySpan<char> rest = line[(sasl + 4)..].TrimStart();
            int space = rest.IndexOf(' ');
            return space < 0 ? null : rest[(space + 1)..].Trim().ToString();
        }

        /// <summary>
        /// Decodes a base64-encoded string.
        /// </summary>
        /// <param name="value">String to decode.</param>
        /// <returns>The decoded string.</returns>
        private static string DecodeMaybeBase64(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return value;
            }
        }
    }

    /// <summary>
    /// Helper to extract SCRAM username from client-first message.
    /// </summary>
    internal static class ScramMechanismBegin
    {
        /// <summary>
        /// Parses the username (n=) from client-first.
        /// </summary>
        /// <param name="clientFirst">Client-first message.</param>
        /// <param name="username">Extracted username.</param>
        /// <returns><see langword="true"/> when found.</returns>
        internal static bool TryGetUsername(string clientFirst, [NotNullWhen(true)] out string? username)
        {
            foreach (string part in clientFirst.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("n=", StringComparison.Ordinal))
                {
                    username = part[2..];
                    return true;
                }
            }

            username = null;
            return false;
        }
    }
}
