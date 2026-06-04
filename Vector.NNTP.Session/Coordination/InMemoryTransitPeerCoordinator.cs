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
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, double>> _peers = new(StringComparer.Ordinal);

        /// <inheritdoc />
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

        /// <inheritdoc />
        public ValueTask ReleaseAsync(string peerId, string sessionId, string nodeName, CancellationToken cancellationToken)
        {
            _ = nodeName;
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            if (_peers.TryGetValue(peerId, out ConcurrentDictionary<string, double>? sessions))
            {
                _ = sessions.TryRemove(sessionId, out _);
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask RefreshLeaseAsync(
            string peerId,
            string sessionId,
            string nodeName,
            int leaseSeconds,
            CancellationToken cancellationToken)
        {
            _ = nodeName;
            _ = leaseSeconds;
            ArgumentException.ThrowIfNullOrEmpty(peerId);
            ArgumentException.ThrowIfNullOrEmpty(sessionId);
            if (_peers.TryGetValue(peerId, out ConcurrentDictionary<string, double>? sessions) &&
                sessions.ContainsKey(sessionId))
            {
                sessions[sessionId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<long> ReconcileCapacityAsync(string peerId, CancellationToken cancellationToken)
        {
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
