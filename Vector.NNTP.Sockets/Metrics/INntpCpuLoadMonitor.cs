// <copyright file="INntpCpuLoadMonitor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// HOT PATH: lock-free CPU overload gate read by accept and dispatch paths.

namespace Vector.NNTP.Sockets.Metrics
{
    /// <summary>
    /// Exposes a hysteresis CPU overload gate driven by EWMA-smoothed utilization signals.
    /// </summary>
    /// <remarks>
    /// Gating is best-effort: hot paths perform a single <see cref="Volatile.Read"/> without locks.
    /// Occasional slip-through during gate transitions is acceptable.
    /// </remarks>
    public interface INntpCpuLoadMonitor
    {
        /// <summary>
        /// Gets a value indicating whether the gate is in the rejecting state.
        /// </summary>
        /// <returns><see langword="true"/> when new connections and commands should receive <c>400</c> and close.</returns>
        bool IsOverloaded();

        /// <summary>
        /// Captures the current EWMA snapshot for structured reject logging and metrics.
        /// </summary>
        /// <returns>Point-in-time utilization and gate state.</returns>
        NntpCpuLoadSnapshot GetSnapshot();

        /// <summary>
        /// Samples enabled CPU signals, blends EWMAs, and updates the hysteresis gate.
        /// </summary>
        /// <remarks>Called periodically from <see cref="Hosting.NntpCpuLoadSamplerHostedService"/>.</remarks>
        void RecordSample();
    }
}
