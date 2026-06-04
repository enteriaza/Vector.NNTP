// <copyright file="TransitPeerCapacityRegistry.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Collections.Concurrent;

namespace Vector.NNTP.Session.Coordination
{
    /// <summary>
    /// Process-wide cache of cluster transit peer capacity for observable gauge export.
    /// </summary>
    public static class TransitPeerCapacityRegistry
    {
        private static readonly ConcurrentDictionary<string, long> CurrentCapacity = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, long> MaxConnections = new(StringComparer.Ordinal);

        /// <summary>
        /// Updates the cached live session count for <paramref name="peerId"/>.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="count">Live count after stale purge.</param>
        public static void UpdateCurrentCapacity(string peerId, long count)
        {
            CurrentCapacity[peerId] = count;
        }

        /// <summary>
        /// Updates configured maximum connections for <paramref name="peerId"/>.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="maxConnections">Configured cap (0 = unlimited).</param>
        public static void UpdateMaxConnections(string peerId, long maxConnections)
        {
            MaxConnections[peerId] = maxConnections;
        }

        /// <summary>
        /// Replaces configured max connection entries for all peers.
        /// </summary>
        /// <param name="peerIdsAndMax">Peer id to max connection map.</param>
        public static void ReplaceConfiguredMax(IReadOnlyDictionary<string, int> peerIdsAndMax)
        {
            foreach (string key in MaxConnections.Keys)
            {
                if (!peerIdsAndMax.ContainsKey(key))
                {
                    _ = MaxConnections.TryRemove(key, out _);
                    _ = CurrentCapacity.TryRemove(key, out _);
                }
            }

            foreach (KeyValuePair<string, int> pair in peerIdsAndMax)
            {
                MaxConnections[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        /// Tries to read the cached live session count for <paramref name="peerId"/>.
        /// </summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="count">Cached count when present.</param>
        /// <returns><see langword="true"/> when a cached value exists.</returns>
        public static bool TryGetCurrentCapacity(string peerId, out long count)
        {
            return CurrentCapacity.TryGetValue(peerId, out count);
        }

        /// <summary>
        /// Gets a snapshot of current capacity measurements for metrics export.
        /// </summary>
        /// <returns>Peer id to count pairs.</returns>
        public static IReadOnlyDictionary<string, long> GetCurrentCapacitySnapshot()
        {
            return CurrentCapacity;
        }

        /// <summary>
        /// Gets a snapshot of configured max connection measurements.
        /// </summary>
        /// <returns>Peer id to max pairs.</returns>
        public static IReadOnlyDictionary<string, long> GetMaxConnectionsSnapshot()
        {
            return MaxConnections;
        }
    }
}
