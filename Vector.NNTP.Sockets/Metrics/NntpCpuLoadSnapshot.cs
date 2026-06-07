// <copyright file="NntpCpuLoadSnapshot.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: point-in-time CPU overload gate snapshot for logging.

namespace Vector.NNTP.Sockets.Metrics
{
    /// <summary>
    /// Point-in-time CPU utilization EWMA values and hysteresis gate state.
    /// </summary>
    /// <param name="ProcessEwmaPercent">Process signal EWMA percent, or <see langword="null"/> when disabled/unseeded.</param>
    /// <param name="HostEwmaPercent">Host-wide signal EWMA percent, or <see langword="null"/> when disabled/unavailable.</param>
    /// <param name="CgroupEwmaPercent">Cgroup quota signal EWMA percent, or <see langword="null"/> when disabled/unavailable.</param>
    /// <param name="EffectiveEwmaPercent">Gate driver: maximum enabled source EWMA.</param>
    /// <param name="DominantSignal">Signal name with the highest EWMA among enabled sources.</param>
    /// <param name="GateState">Gate label: <c>accepting</c> or <c>rejecting</c>.</param>
    /// <param name="RejectThresholdPercent">Configured enter-reject threshold.</param>
    /// <param name="ResumeThresholdPercent">Configured resume-accept threshold.</param>
    public readonly record struct NntpCpuLoadSnapshot(
        double? ProcessEwmaPercent,
        double? HostEwmaPercent,
        double? CgroupEwmaPercent,
        double EffectiveEwmaPercent,
        string DominantSignal,
        string GateState,
        double RejectThresholdPercent,
        double ResumeThresholdPercent);
}
