// <copyright file="SessionContext.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Net;

namespace Vector.NNTP.Session.Context
{
    /// <summary>
    /// Node-local session record stored from TCP accept until disconnect.
    /// </summary>
    /// <remarks>
    /// Authentication transitions use compare-exchange for CAS semantics.
    /// Connection session lifetime equals TCP lifetime; Redis slot lifetime is a subset after admission.
    /// </remarks>
    public sealed class SessionContext
    {
        /// <summary>
        /// Authentication state.
        /// </summary>
        private int _authenticationState;
        
        /// <summary>
        /// Received bytes.
        /// </summary>
        private long _rxBytes;
        
        /// <summary>
        /// Sent bytes.
        /// </summary>
        private long _txBytes;
        
        /// <summary>
        /// Last activity timestamp.
        /// </summary>
        private long _lastActivityUnixSeconds;

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionContext"/> class.
        /// </summary>
        /// <param name="sessionId">Stable session identifier.</param>
        /// <param name="remoteIp">Effective client IP (post-PROXY when applicable).</param>
        /// <param name="connectedAtUtc">Connection timestamp.</param>
        /// <param name="configVersion">Configuration version stamped at accept time.</param>
        public SessionContext(string sessionId, IPAddress remoteIp, DateTimeOffset connectedAtUtc, string configVersion)
        {
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentNullException.ThrowIfNull(remoteIp);
            ArgumentException.ThrowIfNullOrEmpty(configVersion);
            SessionId = sessionId;
            RemoteIp = remoteIp;
            ConnectedAtUtc = connectedAtUtc;
            ConfigVersion = configVersion;
            _authenticationState = (int)AuthenticationState.Unauthenticated;
            _lastActivityUnixSeconds = connectedAtUtc.ToUnixTimeSeconds();
        }

        /// <summary>
        /// Gets the stable session identifier.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// Gets the effective client IP address.
        /// </summary>
        public IPAddress RemoteIp { get; }

        /// <summary>
        /// Gets the UTC time the connection was accepted.
        /// </summary>
        public DateTimeOffset ConnectedAtUtc { get; }

        /// <summary>
        /// Gets the configuration version stamped at session creation.
        /// </summary>
        public string ConfigVersion { get; }

        /// <summary>
        /// Gets the current authentication state.
        /// </summary>
        public AuthenticationState AuthenticationState => (AuthenticationState)Volatile.Read(ref _authenticationState);

        /// <summary>
        /// Gets the optional authenticating sub-phase for logs and metrics.
        /// </summary>
        public AuthenticatingPhase AuthenticatingPhase { get; private set; }

        /// <summary>
        /// Gets the authenticated username, if authenticated or pending admission.
        /// </summary>
        public string? PrincipalUsername { get; private set; }

        /// <summary>
        /// Gets the normalized account key (BLAKE3 hex digest).
        /// </summary>
        public string? AccountKey { get; private set; }

        /// <summary>
        /// Gets the current session policy when bound during authentication.
        /// </summary>
        public NntpSessionPolicy? SessionPolicy { get; private set; }

        /// <summary>
        /// Gets application-level bytes received (RX) for this session.
        /// </summary>
        public long RxBytes => Interlocked.Read(ref _rxBytes);

        /// <summary>
        /// Gets application-level bytes sent (TX) for this session.
        /// </summary>
        public long TxBytes => Interlocked.Read(ref _txBytes);

        /// <summary>
        /// Gets the last activity timestamp (UTC) recorded when bytes were accounted.
        /// </summary>
        public DateTimeOffset LastActivityUtc => DateTimeOffset.FromUnixTimeSeconds(Interlocked.Read(ref _lastActivityUnixSeconds));

        /// <summary>
        /// Adds received bytes and updates last activity time.
        /// </summary>
        /// <param name="bytes">Byte count.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        public void AddRxBytes(long bytes, DateTimeOffset nowUtc)
        {
            if (bytes > 0)
            {
                _ = Interlocked.Add(ref _rxBytes, bytes);
                _ = Interlocked.Exchange(ref _lastActivityUnixSeconds, nowUtc.ToUnixTimeSeconds());
            }
        }

        /// <summary>
        /// Adds sent bytes and updates last activity time.
        /// </summary>
        /// <param name="bytes">Byte count.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        public void AddTxBytes(long bytes, DateTimeOffset nowUtc)
        {
            if (bytes > 0)
            {
                _ = Interlocked.Add(ref _txBytes, bytes);
                _ = Interlocked.Exchange(ref _lastActivityUnixSeconds, nowUtc.ToUnixTimeSeconds());
            }
        }

        /// <summary>
        /// Attempts to transition from <see cref="AuthenticationState.Unauthenticated"/> to <see cref="AuthenticationState.Authenticating"/>.
        /// </summary>
        /// <param name="phase">Authenticating sub-phase for observability.</param>
        /// <returns><see langword="true"/> when the transition succeeded.</returns>
        public bool TryBeginAuthenticating(AuthenticatingPhase phase)
        {
            int prev = Interlocked.CompareExchange(
                ref _authenticationState,
                (int)AuthenticationState.Authenticating,
                (int)AuthenticationState.Unauthenticated);
            if (prev != (int)AuthenticationState.Unauthenticated)
            {
                return false;
            }

            AuthenticatingPhase = phase;
            return true;
        }

        /// <summary>
        /// Binds principal and policy while in <see cref="AuthenticationState.Authenticating"/> (pending admission).
        /// </summary>
        /// <param name="username">Authenticated username.</param>
        /// <param name="accountKey">Normalized account key.</param>
        /// <param name="policy">Session policy awaiting admission.</param>
        /// <param name="phase">Phase to record (typically <see cref="AuthenticatingPhase.PendingAdmission"/>).</param>
        /// <returns><see langword="true"/> when still authenticating and fields were bound.</returns>
        public bool TryBindPendingAuthentication(string username, string accountKey, NntpSessionPolicy policy, AuthenticatingPhase phase)
        {
            ArgumentException.ThrowIfNullOrEmpty(username);
            ArgumentException.ThrowIfNullOrEmpty(accountKey);
            ArgumentNullException.ThrowIfNull(policy);

            if (AuthenticationState != AuthenticationState.Authenticating)
            {
                return false;
            }

            PrincipalUsername = username;
            AccountKey = accountKey;
            SessionPolicy = policy;
            AuthenticatingPhase = phase;
            return true;
        }

        /// <summary>
        /// Completes authentication after successful distributed admission.
        /// </summary>
        /// <returns><see langword="true"/> when transitioned from <see cref="AuthenticationState.Authenticating"/>.</returns>
        public bool TryCompleteAuthentication()
        {
            int prev = Interlocked.CompareExchange(
                ref _authenticationState,
                (int)AuthenticationState.Authenticated,
                (int)AuthenticationState.Authenticating);
            if (prev != (int)AuthenticationState.Authenticating)
            {
                return false;
            }

            AuthenticatingPhase = AuthenticatingPhase.None;
            return true;
        }

        /// <summary>
        /// Rolls back from <see cref="AuthenticationState.Authenticating"/> to <see cref="AuthenticationState.Unauthenticated"/>.
        /// </summary>
        /// <returns><see langword="true"/> when rollback succeeded.</returns>
        public bool TryRollbackAuthenticating()
        {
            int prev = Interlocked.CompareExchange(
                ref _authenticationState,
                (int)AuthenticationState.Unauthenticated,
                (int)AuthenticationState.Authenticating);
            if (prev != (int)AuthenticationState.Authenticating)
            {
                return false;
            }

            PrincipalUsername = null;
            AccountKey = null;
            SessionPolicy = null;
            AuthenticatingPhase = AuthenticatingPhase.None;
            return true;
        }

        /// <summary>
        /// Attempts to transition from authenticated (or authenticating) back to unauthenticated.
        /// </summary>
        /// <returns><see langword="true"/> when a state change occurred.</returns>
        public bool TryDeauthorize()
        {
            int prev = Volatile.Read(ref _authenticationState);
            if (prev == (int)AuthenticationState.Unauthenticated)
            {
                return false;
            }

            _ = Interlocked.Exchange(ref _authenticationState, (int)AuthenticationState.Unauthenticated);
            PrincipalUsername = null;
            AccountKey = null;
            SessionPolicy = null;
            AuthenticatingPhase = AuthenticatingPhase.None;
            return true;
        }
    }
}
