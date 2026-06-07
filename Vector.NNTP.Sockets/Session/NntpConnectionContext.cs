// <copyright file="NntpConnectionContext.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: per-connection identity, byte accounting, and authentication flags.

using System.Diagnostics;
using Vector.NNTP.Sockets.HostProfile;
using Vector.NNTP.Utilities.Diagnostics;

namespace Vector.NNTP.Sockets.Session
{
    /// <summary>
    /// Per-connection identity and byte accounting for logging, metrics, and authentication state.
    /// </summary>
    public sealed class NntpConnectionContext
    {
        private long _rxBytes;
        private long _txBytes;
        private long _commandDispatchStartTimestamp;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpConnectionContext"/> class.
        /// </summary>
        /// <param name="sessionId">Stable session identifier.</param>
        /// <param name="clientRemoteEndPoint">Effective client endpoint (post-PROXY).</param>
        /// <param name="proxyHopEndPoint">TCP peer (first hop).</param>
        /// <param name="hostRole">Reader or transit role.</param>
        /// <param name="nodeName">Stable cluster node identity that accepted the connection.</param>
        /// <param name="transitPeerName">Configured transit peer name when Redis admission succeeded.</param>
        /// <param name="transitPeerMatchedEntry">AcceptFrom entry that matched the peer address, when known.</param>
        public NntpConnectionContext(
            string sessionId,
            IPEndPoint clientRemoteEndPoint,
            IPEndPoint proxyHopEndPoint,
            NntpHostRole hostRole,
            string nodeName,
            string? transitPeerName = null,
            string? transitPeerMatchedEntry = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            ArgumentNullException.ThrowIfNull(clientRemoteEndPoint);
            ArgumentNullException.ThrowIfNull(proxyHopEndPoint);
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            SessionId = sessionId;
            ClientRemoteEndPoint = clientRemoteEndPoint;
            ProxyHopEndPoint = proxyHopEndPoint;
            HostRole = hostRole;
            NodeName = nodeName;
            TransitPeerName = transitPeerName;
            TransitPeerMatchedEntry = transitPeerMatchedEntry;
            SessionStartedUtc = DateTimeOffset.UtcNow;
            ConnectionLogPrefix = FormattingUtilities.FormatConnectionLogPrefix(clientRemoteEndPoint);
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
        /// Gets the bracketed <c>[ip:port]</c> prefix used to correlate RX/TX log lines for this connection.
        /// </summary>
        /// <remarks>
        /// Computed once at construction from <see cref="ClientRemoteEndPoint"/> via
        /// <see cref="FormattingUtilities.FormatConnectionLogPrefix(IPEndPoint)"/>.
        /// </remarks>
        public string ConnectionLogPrefix { get; }

        /// <summary>
        /// Gets the TCP remote endpoint (load balancer or direct client).
        /// </summary>
        public IPEndPoint ProxyHopEndPoint { get; }

        /// <summary>
        /// Gets the host deployment role for this connection.
        /// </summary>
        public NntpHostRole HostRole { get; }

        /// <summary>
        /// Gets the stable cluster node identity that accepted this connection.
        /// </summary>
        public string NodeName { get; }

        /// <summary>
        /// Gets the UTC time when the session was established.
        /// </summary>
        public DateTimeOffset SessionStartedUtc { get; }

        /// <summary>
        /// Gets the configured transit peer name when this connection was admitted as a trusted transit peer.
        /// </summary>
        public string? TransitPeerName { get; }

        /// <summary>
        /// Gets the <c>AcceptFrom</c> configuration entry that matched this peer (literal IP, CIDR, or hostname text).
        /// </summary>
        /// <remarks>
        /// Populated at accept time for trusted transit peers. Used when resolving peer hostnames for SpamAssassin scan
        /// header synthesis without repeating DNS policy work on the writer path.
        /// </remarks>
        public string? TransitPeerMatchedEntry { get; }

        /// <summary>
        /// Gets a value indicating whether this connection is a trusted transit peer (match + Redis admission).
        /// </summary>
        public bool IsTransitPeer => !string.IsNullOrEmpty(TransitPeerName);

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
        /// Gets a value indicating whether a distributed Redis admission slot was acquired.
        /// </summary>
        public bool AdmissionAcquired { get; private set; }

        /// <summary>
        /// Gets total bytes received on the wire including CRLF.
        /// </summary>
        public long RxBytes => Interlocked.Read(ref _rxBytes);

        /// <summary>
        /// Gets total bytes sent on the wire including CRLF.
        /// </summary>
        public long TxBytes => Interlocked.Read(ref _txBytes);

        /// <summary>
        /// Marks the start of command dispatch for elapsed-time measurement on subsequent TX logs.
        /// </summary>
        /// <remarks>
        /// Called at the beginning of each <c>DispatchBytesAsync</c> invocation. The session loop is single-threaded per
        /// connection; no locking is required.
        /// </remarks>
        public void BeginCommandDispatch()
        {
            _commandDispatchStartTimestamp = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Attempts to compute elapsed milliseconds since the most recent <see cref="BeginCommandDispatch"/> call.
        /// </summary>
        /// <param name="milliseconds">Elapsed time in milliseconds when this method returns <see langword="true"/>.</param>
        /// <returns>
        /// <see langword="false"/> when no command dispatch has started (for example before the first client command);
        /// otherwise <see langword="true"/>.
        /// </returns>
        public bool TryGetCommandDispatchElapsedMilliseconds(out double milliseconds)
        {
            long start = _commandDispatchStartTimestamp;
            if (start == 0)
            {
                milliseconds = 0;
                return false;
            }

            milliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            return true;
        }

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
        /// <param name="admissionAcquired">Whether a distributed admission slot was acquired.</param>
        public void SetAuthenticated(NntpSessionPolicy policy, bool admissionAcquired = false)
        {
            ArgumentNullException.ThrowIfNull(policy);
            IsAuthenticated = true;
            AuthenticatedUsername = policy.Username;
            Policy = policy;
            AdmissionAcquired = admissionAcquired;
        }

        /// <summary>
        /// Clears authentication state (for example after quota exhaustion).
        /// </summary>
        public void ClearAuthentication()
        {
            IsAuthenticated = false;
            AuthenticatedUsername = null;
            Policy = null;
            AdmissionAcquired = false;
        }
    }
}
