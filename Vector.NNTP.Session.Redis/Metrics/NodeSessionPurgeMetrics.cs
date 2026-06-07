// <copyright file="NodeSessionPurgeMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Diagnostics.Metrics;

namespace Vector.NNTP.Session.Redis.Metrics
{
    /// <summary>
    /// OpenTelemetry metrics for node-scoped session purge operations.
    /// </summary>
    public static class NodeSessionPurgeMetrics
    {
        /// <summary>
        /// OpenTelemetry meter for node purge instrumentation in the session assembly.
        /// </summary>
        private static readonly Meter Meter = new("Vector.NNTP.Session", "1.0.0");

        /// <summary>
        /// Counter of authenticated session leases released during node purge.
        /// </summary>
        private static readonly Counter<long> AuthLeasesPurgedCounter =
            Meter.CreateCounter<long>("nntp.node.purge.auth_leases");

        /// <summary>
        /// Counter of transit peer session leases released during node purge.
        /// </summary>
        private static readonly Counter<long> TransitLeasesPurgedCounter =
            Meter.CreateCounter<long>("nntp.node.purge.transit_leases");

        /// <summary>
        /// Histogram of node purge wall-clock duration in milliseconds.
        /// </summary>
        private static readonly Histogram<double> DurationMsHistogram =
            Meter.CreateHistogram<double>("nntp.node.purge.duration_ms");

        /// <summary>Records auth leases purged for a node.</summary>
        /// <param name="nodeName">Stable node identity.</param>
        /// <param name="count">Leases purged.</param>
        public static void RecordAuthPurged(string nodeName, long count)
        {
            if (count > 0)
            {
                AuthLeasesPurgedCounter.Add(count, new KeyValuePair<string, object?>("node", nodeName));
            }
        }

        /// <summary>Records transit leases purged for a node.</summary>
        /// <param name="nodeName">Stable node identity.</param>
        /// <param name="count">Leases purged.</param>
        public static void RecordTransitPurged(string nodeName, long count)
        {
            if (count > 0)
            {
                TransitLeasesPurgedCounter.Add(count, new KeyValuePair<string, object?>("node", nodeName));
            }
        }

        /// <summary>Records purge wall-clock duration in milliseconds.</summary>
        /// <param name="nodeName">Stable node identity.</param>
        /// <param name="durationMs">Duration in milliseconds.</param>
        public static void RecordDuration(string nodeName, double durationMs)
        {
            DurationMsHistogram.Record(durationMs, new KeyValuePair<string, object?>("node", nodeName));
        }
    }
}
