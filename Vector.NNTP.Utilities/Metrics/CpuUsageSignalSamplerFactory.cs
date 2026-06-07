// <copyright file="CpuUsageSignalSamplerFactory.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: composes platform CPU utilization samplers.

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Composes the default <see cref="ICpuUsageSignalSampler"/> set for CPU overload monitoring on the current OS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <c>NntpCpuLoadMonitor</c> to obtain platform readers without pulling socket-layer dependencies
    /// into sampler construction. Each returned sampler exposes a stable <see cref="ICpuUsageSignalSampler.SignalName"/>
    /// from <see cref="CpuUsageSignalNames"/> and reports runtime availability via
    /// <see cref="ICpuUsageSignalSampler.IsAvailable"/> (for example cgroup quota absent on non-container hosts).
    /// </para>
    /// <para>Platform composition:</para>
    /// <list type="bullet">
    /// <item><description>All platforms — <see cref="ProcessCpuUsageSampler"/> (<see cref="CpuUsageSignalNames.Process"/>).</description></item>
    /// <item><description>Linux — <see cref="LinuxHostCpuUsageSampler"/> and <see cref="LinuxCgroupCpuUsageSampler"/>.</description></item>
    /// <item><description>Windows — <see cref="WindowsHostCpuUsageSampler"/>.</description></item>
    /// <item><description>Other operating systems — process sampler only.</description></item>
    /// </list>
    /// </remarks>
    public static class CpuUsageSignalSamplerFactory
    {
        /// <summary>
        /// Creates the default process, host, and (on Linux) cgroup samplers for the current platform.
        /// </summary>
        /// <returns>
        /// A non-empty read-only list. Always includes <see cref="ProcessCpuUsageSampler"/>.
        /// Adds host-wide samplers on Linux and Windows; adds <see cref="LinuxCgroupCpuUsageSampler"/> on Linux only.
        /// </returns>
        /// <remarks>
        /// Callers enable or disable individual signals at the monitor/options layer; this factory only supplies
        /// candidate implementations. Samplers that are not applicable at runtime remain in the list but return
        /// <see langword="false"/> from <see cref="ICpuUsageSignalSampler.TrySample"/> or report
        /// <see cref="ICpuUsageSignalSampler.IsAvailable"/> as <see langword="false"/>.
        /// </remarks>
        public static IReadOnlyList<ICpuUsageSignalSampler> CreateDefault()
        {
            List<ICpuUsageSignalSampler> samplers =
            [
                new ProcessCpuUsageSampler(),
            ];

            if (OperatingSystem.IsLinux())
            {
                samplers.Add(new LinuxHostCpuUsageSampler());
                samplers.Add(new LinuxCgroupCpuUsageSampler());
            }
            else if (OperatingSystem.IsWindows())
            {
                samplers.Add(new WindowsHostCpuUsageSampler());
            }

            return samplers;
        }
    }
}
