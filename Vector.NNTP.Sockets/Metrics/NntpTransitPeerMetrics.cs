// <copyright file="NntpTransitPeerMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: OpenTelemetry instruments for trusted transit peer admission and DNS refresh.

using System.Diagnostics.Metrics;
using Vector.NNTP.Sockets.Configuration;

namespace Vector.NNTP.Sockets.Metrics
{
    /// <summary>
    /// OpenTelemetry metrics for NNTP trusted transit peers (bounded <c>peer</c> label = <see cref="NntpTransitPeerOptions.Name"/>).
    /// </summary>
    public static class NntpTransitPeerMetrics
    {
        private static readonly Meter Meter = new("Vector.NNTP.Sockets", "1.0.0");

        private static readonly Counter<long> MatchesCounter =
            Meter.CreateCounter<long>("nntp.transitpeer.matches_total");

        private static readonly Counter<long> AcquireFailuresCounter =
            Meter.CreateCounter<long>("nntp.transitpeer.acquire_failures_total");

        private static readonly Counter<long> RedisErrorsCounter =
            Meter.CreateCounter<long>("nntp.transitpeer.redis_errors_total");

        private static readonly Counter<long> RefreshSuccessCounter =
            Meter.CreateCounter<long>("nntp.transitpeer.refresh_success_total");

        private static readonly Counter<long> RefreshFailureCounter =
            Meter.CreateCounter<long>("nntp.transitpeer.refresh_failure_total");

        private static readonly Counter<long> CheckWithoutAuthCounter =
            Meter.CreateCounter<long>("nntp.transitpeer.check.without_auth_total");

        private static readonly Counter<long> TakethisWithoutAuthCounter =
            Meter.CreateCounter<long>("nntp.transitpeer.takethis.without_auth_total");

        private static readonly Counter<long> IhaveWithoutAuthCounter =
            Meter.CreateCounter<long>("nntp.transitpeer.ihave.without_auth_total");

        private static readonly UpDownCounter<long> ActiveConnectionsCounter =
            Meter.CreateUpDownCounter<long>("nntp.transitpeer.active_connections");

        static NntpTransitPeerMetrics()
        {
            _ = Meter.CreateObservableGauge(
                "nntp.transitpeer.current_capacity",
                ObserveCurrentCapacity,
                description: "Cluster-wide live transit peer sessions (post stale purge).");

            _ = Meter.CreateObservableGauge(
                "nntp.transitpeer.max_connections",
                ObserveMaxConnections,
                description: "Configured MaxConnections per peer (0 = unlimited).");
        }

        /// <summary>Records a successful transit peer match and Redis admission.</summary>
        /// <param name="peerId">Stable peer identifier.</param>
        public static void RecordMatch(string peerId)
        {
            MatchesCounter.Add(1, new KeyValuePair<string, object?>("peer", peerId));
        }

        /// <summary>Records admission failure because the peer is at capacity.</summary>
        /// <param name="peerId">Stable peer identifier.</param>
        public static void RecordAcquireFailure(string peerId)
        {
            AcquireFailuresCounter.Add(1, new KeyValuePair<string, object?>("peer", peerId));
        }

        /// <summary>Records a Redis or coordination backend error.</summary>
        /// <param name="peerId">Stable peer identifier, or <c>unknown</c> when not applicable.</param>
        public static void RecordRedisError(string peerId)
        {
            RedisErrorsCounter.Add(1, new KeyValuePair<string, object?>("peer", peerId));
        }

        /// <summary>Records a successful DNS snapshot refresh.</summary>
        public static void RecordRefreshSuccess()
        {
            RefreshSuccessCounter.Add(1);
        }

        /// <summary>Records a failed DNS snapshot refresh.</summary>
        /// <param name="reason">Bounded reason token (<c>dns</c>, <c>overlap</c>, <c>parse</c>).</param>
        public static void RecordRefreshFailure(string reason)
        {
            RefreshFailureCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));
        }

        /// <summary>Adjusts the node-local active connection counter.</summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="delta">+1 on admit, -1 on teardown.</param>
        public static void RecordActiveConnection(string peerId, int delta)
        {
            ActiveConnectionsCounter.Add(delta, new KeyValuePair<string, object?>("peer", peerId));
        }

        /// <summary>Records CHECK allowed without authentication for a trusted transit peer.</summary>
        /// <param name="peerId">Stable peer identifier.</param>
        public static void RecordCheckWithoutAuth(string peerId)
        {
            CheckWithoutAuthCounter.Add(1, new KeyValuePair<string, object?>("peer", peerId));
        }

        /// <summary>Records TAKETHIS allowed without authentication.</summary>
        /// <param name="peerId">Stable peer identifier.</param>
        public static void RecordTakethisWithoutAuth(string peerId)
        {
            TakethisWithoutAuthCounter.Add(1, new KeyValuePair<string, object?>("peer", peerId));
        }

        /// <summary>Records IHAVE allowed without authentication.</summary>
        /// <param name="peerId">Stable peer identifier.</param>
        public static void RecordIhaveWithoutAuth(string peerId)
        {
            IhaveWithoutAuthCounter.Add(1, new KeyValuePair<string, object?>("peer", peerId));
        }

        /// <summary>Updates cached cluster capacity for observable gauge export.</summary>
        /// <param name="peerId">Stable peer identifier.</param>
        /// <param name="count">Live session count.</param>
        public static void UpdateCurrentCapacity(string peerId, long count)
        {
            TransitPeerCapacityRegistry.UpdateCurrentCapacity(peerId, count);
        }

        /// <summary>Updates configured max connections per peer for observable gauge export.</summary>
        /// <param name="peers">Configured peers.</param>
        public static void UpdateConfiguredCapacity(IReadOnlyList<NntpTransitPeerOptions> peers)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (NntpTransitPeerOptions peer in peers)
            {
                map[peer.Name] = peer.MaxConnections;
            }

            TransitPeerCapacityRegistry.ReplaceConfiguredMax(map);
        }

        private static IEnumerable<Measurement<long>> ObserveCurrentCapacity()
        {
            foreach (KeyValuePair<string, long> pair in TransitPeerCapacityRegistry.GetCurrentCapacitySnapshot())
            {
                yield return new Measurement<long>(pair.Value, new KeyValuePair<string, object?>("peer", pair.Key));
            }
        }

        private static IEnumerable<Measurement<long>> ObserveMaxConnections()
        {
            foreach (KeyValuePair<string, long> pair in TransitPeerCapacityRegistry.GetMaxConnectionsSnapshot())
            {
                yield return new Measurement<long>(pair.Value, new KeyValuePair<string, object?>("peer", pair.Key));
            }
        }
    }
}
