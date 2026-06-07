// <copyright file="ICpuUsageSignalSampler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Samples one CPU utilization signal over repeated intervals.
    /// </summary>
    /// <remarks>
    /// Implementations retain prior sample state internally. The first successful interval seeds the series;
    /// subsequent calls return utilization percent for the elapsed window.
    /// </remarks>
    public interface ICpuUsageSignalSampler
    {
        /// <summary>
        /// Gets the stable signal name (<see cref="CpuUsageSignalNames"/>).
        /// </summary>
        public string SignalName { get; }

        /// <summary>
        /// Gets a value indicating whether this signal can contribute to overload gating on this host.
        /// </summary>
        public bool IsAvailable { get; }

        /// <summary>
        /// Attempts to sample utilization for the interval since the prior call.
        /// </summary>
        /// <param name="utilizationPercent">Raw utilization percent in [0, 100] when successful.</param>
        /// <returns><see langword="true"/> when a new sample was produced.</returns>
        public bool TrySample(out double utilizationPercent);
    }
}
