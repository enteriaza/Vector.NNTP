// <copyright file="InMemoryTransitPeerCoordinator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Collections.Concurrent;

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// In-memory transit peer coordinator for unit tests (ZSET semantics: score = Unix seconds).
    /// </summary>
    public sealed class InMemoryTransitPeerCoordinator : INntpTransitPeerCoordinator
    {
        /// <summary>
        /// Peer identifier to session-id map with last-refresh Unix scores (ZSET stand-in).
        /// </summary>
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, double>> _peers = new(StringComparer.Ordinal);

        /// <summary>
        /// Attempts to acquire or refresh a peer session slot after purging stale scores.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Globally unique session identifier.</param>
        /// <param name="maxConnections">Configured cap (0 = unlimited).</param>
        /// <param name="leaseSeconds">Stale score cutoff and lease extension window.</param>
        /// <param name="nodeName">Stable cluster node identity (ignored in-memory).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see cref="NntpTransitPeerAdmissionResult.Success"/> or <see cref="NntpTransitPeerAdmissionResult.AtCapacity"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="peerId"/> or <paramref name="sessionId"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Propagated when <paramref name="cancellationToken"/> is canceled.</exception>
        public ValueTask<NntpTransitPeerAdmissionResult> TryAcquireAsync(
            string peerId,
            string sessionId,
            int maxConnections,
            int leaseSeconds,
            string nodeName,
            CancellationToken cancellationToken)
        {
            _ = nodeName;
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            cancellationToken.ThrowIfCancellationRequested();
            ConcurrentDictionary<string, double> sessions = _peers.GetOrAdd(peerId, static _ => new ConcurrentDictionary<string, double>(StringComparer.Ordinal));
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double cutoff = now - leaseSeconds;
            PurgeStale(sessions, cutoff);
            if (sessions.TryGetValue(sessionId, out _))
            {
                sessions[sessionId] = now;
                return ValueTask.FromResult(NntpTransitPeerAdmissionResult.Success);
            }

            if (maxConnections > 0 && sessions.Count >= maxConnections)
            {
                return ValueTask.FromResult(NntpTransitPeerAdmissionResult.AtCapacity);
            }

            if (!sessions.TryAdd(sessionId, now))
            {
                sessions[sessionId] = now;
            }

            return ValueTask.FromResult(NntpTransitPeerAdmissionResult.Success);
        }

        /// <summary>
        /// Removes a peer session slot when present (idempotent).
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Session identifier used at acquire time.</param>
        /// <param name="nodeName">Stable cluster node identity (ignored in-memory).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed value task.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="peerId"/> or <paramref name="sessionId"/> is null or empty.</exception>
        public ValueTask ReleaseAsync(string peerId, string sessionId, string nodeName, CancellationToken cancellationToken)
        {
            _ = nodeName;
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            if (_peers.TryGetValue(peerId, out ConcurrentDictionary<string, double>? sessions))
            {
                _ = sessions.TryRemove(sessionId, out _);
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Updates the ZSET score for a live peer session when still registered.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="nodeName">Stable cluster node identity (ignored in-memory).</param>
        /// <param name="leaseSeconds">Lease window (ignored except for API parity).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed value task.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="peerId"/> or <paramref name="sessionId"/> is null or empty.</exception>
        public ValueTask RefreshLeaseAsync(
            string peerId,
            string sessionId,
            string nodeName,
            int leaseSeconds,
            CancellationToken cancellationToken)
        {
            _ = nodeName;
            _ = leaseSeconds;
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            if (_peers.TryGetValue(peerId, out ConcurrentDictionary<string, double>? sessions) &&
                sessions.ContainsKey(sessionId))
            {
                sessions[sessionId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Purges stale peer sessions and returns the live count for metrics reconciliation.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Live session count after a 300-second stale purge.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="peerId"/> is null or empty.</exception>
        public ValueTask<long> ReconcileCapacityAsync(string peerId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            if (!_peers.TryGetValue(peerId, out ConcurrentDictionary<string, double>? sessions))
            {
                return ValueTask.FromResult(0L);
            }

            double cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300;
            PurgeStale(sessions, cutoff);
            return ValueTask.FromResult((long)sessions.Count);
        }

        /// <summary>Removes ZSET members whose score is older than the lease cutoff.</summary>
        /// <param name="sessions">Peer session map.</param>
        /// <param name="cutoff">Minimum valid score.</param>
        private static void PurgeStale(ConcurrentDictionary<string, double> sessions, double cutoff)
        {
            foreach (KeyValuePair<string, double> pair in sessions)
            {
                if (pair.Value < cutoff)
                {
                    _ = sessions.TryRemove(pair.Key, out _);
                }
            }
        }
    }
}
