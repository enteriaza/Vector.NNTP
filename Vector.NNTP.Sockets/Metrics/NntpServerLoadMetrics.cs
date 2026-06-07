// <copyright file="NntpServerLoadMetrics.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: OpenTelemetry instruments for CPU overload gating.

using System.Diagnostics.Metrics;

namespace Vector.NNTP.Sockets.Metrics
{
    /// <summary>
    /// OpenTelemetry metrics for CPU utilization EWMA and overload connection rejection.
    /// </summary>
    public static class NntpServerLoadMetrics
    {
        private static readonly Meter Meter = new("Vector.NNTP.Sockets", "1.0.0");

        private static readonly object Sync = new();
        private static double _effectiveEwma = -1;
        private static double _processEwma = -1;
        private static double _hostEwma = -1;
        private static double _cgroupEwma = -1;
        private static int _gateState;
        private static double _rejectThreshold = -1;
        private static double _resumeThreshold = -1;

        private static readonly Counter<long> AcceptRejectCounter =
            Meter.CreateCounter<long>("nntp.server.connections_rejected_cpu_accept_total");

        private static readonly Counter<long> CommandRejectCounter =
            Meter.CreateCounter<long>("nntp.server.connections_rejected_cpu_command_total");

        static NntpServerLoadMetrics()
        {
            _ = Meter.CreateObservableGauge(
                "nntp.server.cpu_utilization_ewma_percent",
                () => Observe(_effectiveEwma),
                description: "Effective CPU utilization EWMA percent (gate driver).");

            _ = Meter.CreateObservableGauge(
                "nntp.server.cpu_utilization_ewma_percent_process",
                () => Observe(_processEwma),
                description: "Process CPU utilization EWMA percent.");

            _ = Meter.CreateObservableGauge(
                "nntp.server.cpu_utilization_ewma_percent_host",
                () => Observe(_hostEwma),
                description: "Host-wide CPU utilization EWMA percent.");

            _ = Meter.CreateObservableGauge(
                "nntp.server.cpu_utilization_ewma_percent_cgroup",
                () => Observe(_cgroupEwma),
                description: "Cgroup quota-relative CPU utilization EWMA percent (-1 when unavailable).");

            _ = Meter.CreateObservableGauge(
                "nntp.server.cpu_gate_state",
                () => new Measurement<int>(_gateState),
                description: "CPU overload gate state (0=accepting, 1=rejecting).");

            _ = Meter.CreateObservableGauge(
                "nntp.server.cpu_reject_threshold_percent",
                () => Observe(_rejectThreshold),
                description: "Configured CPU reject threshold percent.");

            _ = Meter.CreateObservableGauge(
                "nntp.server.cpu_resume_threshold_percent",
                () => Observe(_resumeThreshold),
                description: "Configured CPU resume threshold percent.");
        }

        /// <summary>
        /// Records updated EWMA gauge values from a monitor snapshot.
        /// </summary>
        /// <param name="effectiveEwma">Effective EWMA percent.</param>
        /// <param name="snapshot">Full snapshot for per-source gauges.</param>
        public static void RecordCpuEwma(double effectiveEwma, NntpCpuLoadSnapshot snapshot)
        {
            lock (Sync)
            {
                _effectiveEwma = effectiveEwma;
                _processEwma = snapshot.ProcessEwmaPercent ?? -1;
                _hostEwma = snapshot.HostEwmaPercent ?? -1;
                _cgroupEwma = snapshot.CgroupEwmaPercent ?? -1;
                _rejectThreshold = snapshot.RejectThresholdPercent;
                _resumeThreshold = snapshot.ResumeThresholdPercent;
            }
        }

        /// <summary>
        /// Records the current gate state for observability.
        /// </summary>
        /// <param name="rejecting">Whether the gate is rejecting.</param>
        public static void RecordGateState(bool rejecting)
        {
            lock (Sync)
            {
                _gateState = rejecting ? 1 : 0;
            }
        }

        /// <summary>
        /// Increments the accept-path CPU reject counter.
        /// </summary>
        public static void RecordAcceptReject()
        {
            AcceptRejectCounter.Add(1);
        }

        /// <summary>
        /// Increments the command-path CPU reject counter.
        /// </summary>
        public static void RecordCommandReject()
        {
            CommandRejectCounter.Add(1);
        }

        private static IEnumerable<Measurement<double>> Observe(double value)
        {
            yield return new Measurement<double>(value);
        }
    }
}
