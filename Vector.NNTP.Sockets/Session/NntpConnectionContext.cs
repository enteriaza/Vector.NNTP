// <copyright file="NntpConnectionContext.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: per-connection identity, byte accounting, and authentication flags.

using Vector.NNTP.Sockets.Authentication;
using Vector.NNTP.Sockets.HostProfile;

namespace Vector.NNTP.Sockets.Session
{
    /// <summary>
    /// Per-connection identity and byte accounting for logging, metrics, and authentication state.
    /// </summary>
    public sealed class NntpConnectionContext
    {
        private long _rxBytes;
        private long _txBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpConnectionContext"/> class.
        /// </summary>
        /// <param name="sessionId">Stable session identifier.</param>
        /// <param name="clientRemoteEndPoint">Effective client endpoint (post-PROXY).</param>
        /// <param name="proxyHopEndPoint">TCP peer (first hop).</param>
        /// <param name="hostRole">Reader or transit role.</param>
        public NntpConnectionContext(
            string sessionId,
            IPEndPoint clientRemoteEndPoint,
            IPEndPoint proxyHopEndPoint,
            NntpHostRole hostRole)
        {
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentNullException.ThrowIfNull(clientRemoteEndPoint);
            ArgumentNullException.ThrowIfNull(proxyHopEndPoint);
            SessionId = sessionId;
            ClientRemoteEndPoint = clientRemoteEndPoint;
            ProxyHopEndPoint = proxyHopEndPoint;
            HostRole = hostRole;
            SessionStartedUtc = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Gets the session identifier for metrics and logging.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// Gets the effective client endpoint after PROXY resolution.
        /// </summary>
        public IPEndPoint ClientRemoteEndPoint { get; }

        /// <summary>
        /// Gets the TCP remote endpoint (load balancer or direct client).
        /// </summary>
        public IPEndPoint ProxyHopEndPoint { get; }

        /// <summary>
        /// Gets the host deployment role for this connection.
        /// </summary>
        public NntpHostRole HostRole { get; }

        /// <summary>
        /// Gets the UTC time when the session was established.
        /// </summary>
        public DateTimeOffset SessionStartedUtc { get; }

        /// <summary>
        /// Gets a value indicating whether the client completed NNTP authentication.
        /// </summary>
        public bool IsAuthenticated { get; private set; }

        /// <summary>
        /// Gets the authenticated username when <see cref="IsAuthenticated"/> is true.
        /// </summary>
        public string? AuthenticatedUsername { get; private set; }

        /// <summary>
        /// Gets the policy granted after successful authentication.
        /// </summary>
        public NntpSessionPolicy? Policy { get; private set; }

        /// <summary>
        /// Gets total bytes received on the wire including CRLF.
        /// </summary>
        public long RxBytes => Interlocked.Read(ref _rxBytes);

        /// <summary>
        /// Gets total bytes sent on the wire including CRLF.
        /// </summary>
        public long TxBytes => Interlocked.Read(ref _txBytes);

        /// <summary>
        /// Records received bytes toward <see cref="RxBytes"/>.
        /// </summary>
        /// <param name="count">Byte count including CRLF.</param>
        public void AddRxBytes(int count)
        {
            _ = Interlocked.Add(ref _rxBytes, count);
        }

        /// <summary>
        /// Records transmitted bytes toward <see cref="TxBytes"/>.
        /// </summary>
        /// <param name="count">Byte count including CRLF.</param>
        public void AddTxBytes(int count)
        {
            _ = Interlocked.Add(ref _txBytes, count);
        }

        /// <summary>
        /// Marks the connection authenticated with the given policy.
        /// </summary>
        /// <param name="policy">Granted session policy.</param>
        public void SetAuthenticated(NntpSessionPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            IsAuthenticated = true;
            AuthenticatedUsername = policy.Username;
            Policy = policy;
        }

        /// <summary>
        /// Clears authentication state (for example after explicit de-auth policies in future hosts).
        /// </summary>
        public void ClearAuthentication()
        {
            IsAuthenticated = false;
            AuthenticatedUsername = null;
            Policy = null;
        }
    }
}
