// <copyright file="CpuUsageSignalNames.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: stable CPU overload signal identifiers for samplers, metrics, and logs.

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Canonical string identifiers for CPU utilization signals used in overload gating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <see cref="ICpuUsageSignalSampler"/> returns one of these values from
    /// <see cref="ICpuUsageSignalSampler.SignalName"/>. The CPU load monitor blends per-source EWMAs,
    /// takes the maximum among enabled sources, and records the highest contributor as the dominant signal
    /// in structured reject logs and OpenTelemetry gauges.
    /// </para>
    /// <para>Values are lowercase literals suitable for log fields and metric label dimensions.</para>
    /// </remarks>
    public static class CpuUsageSignalNames
    {
        /// <summary>
        /// Signal name for current-process CPU utilization normalized by logical processor count.
        /// </summary>
        /// <remarks>
        /// Produced by <see cref="ProcessCpuUsageSampler"/>. Available on all supported platforms.
        /// </remarks>
        public const string Process = "process";

        /// <summary>
        /// Signal name for machine-wide host CPU busy percent.
        /// </summary>
        /// <remarks>
        /// Produced by <see cref="LinuxHostCpuUsageSampler"/> on Linux and
        /// <see cref="WindowsHostCpuUsageSampler"/> on Windows.
        /// </remarks>
        public const string Host = "host";

        /// <summary>
        /// Signal name for cgroup quota-relative CPU utilization.
        /// </summary>
        /// <remarks>
        /// Produced by <see cref="LinuxCgroupCpuUsageSampler"/> when a finite cgroup CPU quota exists.
        /// Excluded from gating when quota is unlimited or cgroup files are unavailable.
        /// </remarks>
        public const string Cgroup = "cgroup";
    }
}
