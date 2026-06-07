// <copyright file="CpuUtilizationCalculator.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: pure CPU utilization percentage math for process, host, and cgroup quota signals.

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Pure functions that convert CPU time deltas into utilization percentages for overload sampling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All inputs are expressed in seconds (or second-equivalent counter deltas). Samplers derive deltas from
    /// <see cref="System.Diagnostics.Stopwatch"/> ticks, <see cref="System.Diagnostics.Process.TotalProcessorTime"/>,
    /// Linux jiffies, Win32 FILETIME, or cgroup usage counters before calling these helpers.
    /// </para>
    /// <para>Three formulas are supported:</para>
    /// <list type="bullet">
    /// <item><description><see cref="ComputeProcessPercent"/> — per-process CPU vs logical processors.</description></item>
    /// <item><description><see cref="ComputeHostPercent"/> — machine-wide busy fraction.</description></item>
    /// <item><description><see cref="ComputeCgroupPercent"/> — cgroup usage vs quota cores.</description></item>
    /// </list>
    /// <para>
    /// Successful results pass through <see cref="ClampPercent"/> so raw spikes above 100% or negative noise
    /// do not destabilize downstream EWMA blending.
    /// </para>
    /// </remarks>
    public static class CpuUtilizationCalculator
    {
        /// <summary>
        /// Minimum wall-clock or total-counter elapsed seconds required before a sample is considered valid.
        /// </summary>
        /// <remarks>
        /// Guards against division by near-zero intervals when sample calls occur back-to-back.
        /// </remarks>
        public const double MinimumElapsedSeconds = 0.001;

        /// <summary>
        /// Computes process (or generic per-logical-CPU) utilization percent.
        /// </summary>
        /// <param name="cpuDeltaSeconds">CPU time consumed by the process during the interval.</param>
        /// <param name="elapsedSeconds">Wall-clock elapsed seconds between samples.</param>
        /// <param name="processorCount">Logical processor count (<see cref="Environment.ProcessorCount"/>).</param>
        /// <returns>
        /// <c>cpuDeltaSeconds / (elapsedSeconds * processorCount) * 100</c>, clamped to [0, 100];
        /// <see langword="null"/> when <paramref name="elapsedSeconds"/> is below <see cref="MinimumElapsedSeconds"/>,
        /// <paramref name="processorCount"/> is not positive, or <paramref name="cpuDeltaSeconds"/> is negative.
        /// </returns>
        public static double? ComputeProcessPercent(double cpuDeltaSeconds, double elapsedSeconds, int processorCount)
        {
            if (elapsedSeconds < MinimumElapsedSeconds || processorCount <= 0 || cpuDeltaSeconds < 0)
            {
                return null;
            }

            double percent = cpuDeltaSeconds / (elapsedSeconds * processorCount) * 100.0;
            return ClampPercent(percent);
        }

        /// <summary>
        /// Computes machine-wide host busy percent from busy vs total CPU time deltas.
        /// </summary>
        /// <param name="busyDeltaSeconds">Non-idle CPU time delta (all cores aggregated).</param>
        /// <param name="totalDeltaSeconds">Total CPU time delta (busy plus idle components).</param>
        /// <returns>
        /// <c>busyDeltaSeconds / totalDeltaSeconds * 100</c>, clamped to [0, 100];
        /// <see langword="null"/> when <paramref name="totalDeltaSeconds"/> is below <see cref="MinimumElapsedSeconds"/>
        /// or <paramref name="busyDeltaSeconds"/> is negative.
        /// </returns>
        public static double? ComputeHostPercent(double busyDeltaSeconds, double totalDeltaSeconds)
        {
            if (totalDeltaSeconds < MinimumElapsedSeconds || busyDeltaSeconds < 0)
            {
                return null;
            }

            double percent = busyDeltaSeconds / totalDeltaSeconds * 100.0;
            return ClampPercent(percent);
        }

        /// <summary>
        /// Computes cgroup quota-relative utilization percent.
        /// </summary>
        /// <param name="cpuUsageDeltaSeconds">Cgroup CPU usage accumulated during the interval.</param>
        /// <param name="elapsedSeconds">Wall-clock elapsed seconds between samples.</param>
        /// <param name="quotaCores">Effective CPU cores allowed by cgroup quota (<c>quota / period</c>).</param>
        /// <returns>
        /// <c>cpuUsageDeltaSeconds / (elapsedSeconds * quotaCores) * 100</c>, clamped to [0, 100];
        /// <see langword="null"/> when <paramref name="elapsedSeconds"/> is below <see cref="MinimumElapsedSeconds"/>,
        /// <paramref name="quotaCores"/> is not positive, or <paramref name="cpuUsageDeltaSeconds"/> is negative.
        /// </returns>
        public static double? ComputeCgroupPercent(double cpuUsageDeltaSeconds, double elapsedSeconds, double quotaCores)
        {
            if (elapsedSeconds < MinimumElapsedSeconds || quotaCores <= 0 || cpuUsageDeltaSeconds < 0)
            {
                return null;
            }

            double percent = cpuUsageDeltaSeconds / (elapsedSeconds * quotaCores) * 100.0;
            return ClampPercent(percent);
        }

        /// <summary>
        /// Clamps a raw utilization sample to the inclusive range [0, 100].
        /// </summary>
        /// <param name="percent">Unbounded or noisy utilization percent from a delta ratio.</param>
        /// <returns><see cref="Math.Clamp(double, double, double)"/> of <paramref name="percent"/> between 0 and 100.</returns>
        public static double ClampPercent(double percent)
        {
            return Math.Clamp(percent, 0, 100);
        }
    }
}
