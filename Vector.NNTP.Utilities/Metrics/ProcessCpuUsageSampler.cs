// <copyright file="ProcessCpuUsageSampler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: process CPU utilization sampler.

using System.Diagnostics;

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Samples the current process CPU utilization normalized by logical processor count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements <see cref="ICpuUsageSignalSampler"/> for the <see cref="CpuUsageSignalNames.Process"/> signal.
    /// Each call refreshes <see cref="Process.TotalProcessorTime"/> for the current process, measures wall-clock
    /// elapsed time with <see cref="Stopwatch.GetTimestamp"/>, and passes deltas to
    /// <see cref="CpuUtilizationCalculator.ComputeProcessPercent"/>.
    /// </para>
    /// <para>
    /// The first call captures baselines and returns <see langword="false"/> so the next interval has a defined
    /// delta window. Utilization is expressed as a percentage of one fully busy logical CPU
    /// (<c>cpuDelta / (elapsed * processorCount) * 100</c>), clamped to [0, 100].
    /// </para>
    /// <para>
    /// This sampler is always eligible on hosts where <see cref="Environment.ProcessorCount"/> is positive
    /// (Windows and Linux).
    /// </para>
    /// </remarks>
    public sealed class ProcessCpuUsageSampler : ICpuUsageSignalSampler
    {
        /// <summary>
        /// Handle to the current worker process used for <see cref="Process.TotalProcessorTime"/> reads.
        /// </summary>
        private readonly Process _process = Process.GetCurrentProcess();

        /// <summary>
        /// Logical processor count used as the utilization divisor.
        /// </summary>
        private readonly int _processorCount = Environment.ProcessorCount;

        /// <summary>
        /// Baseline <see cref="Process.TotalProcessorTime"/> from the previous sample.
        /// </summary>
        private TimeSpan _lastCpu;

        /// <summary>
        /// Baseline <see cref="Stopwatch.GetTimestamp"/> tick count from the previous sample.
        /// </summary>
        private long _lastTimestamp;

        /// <summary>
        /// Indicates whether baseline CPU and timestamp values have been captured.
        /// </summary>
        private bool _seeded;

        /// <summary>
        /// Gets the stable process-scoped signal name (<see cref="CpuUsageSignalNames.Process"/>).
        /// </summary>
        public string SignalName => CpuUsageSignalNames.Process;

        /// <summary>
        /// Gets a value indicating whether process CPU sampling can contribute to overload gating.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="true"/> when <see cref="Environment.ProcessorCount"/> is greater than zero.
        /// </remarks>
        public bool IsAvailable => _processorCount > 0;

        /// <summary>
        /// Attempts to sample process CPU utilization for the interval since the prior call.
        /// </summary>
        /// <param name="utilizationPercent">
        /// When this method returns <see langword="true"/>, receives the raw utilization percent in [0, 100]
        /// after clamping by <see cref="CpuUtilizationCalculator.ComputeProcessPercent"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a new utilization sample was produced;
        /// <see langword="false"/> when the series is being seeded, elapsed time is below
        /// <see cref="CpuUtilizationCalculator.MinimumElapsedSeconds"/>, or calculator inputs are invalid.
        /// </returns>
        public bool TrySample(out double utilizationPercent)
        {
            utilizationPercent = 0;
            _process.Refresh();
            TimeSpan cpu = _process.TotalProcessorTime;
            long now = Stopwatch.GetTimestamp();

            if (!_seeded)
            {
                _lastCpu = cpu;
                _lastTimestamp = now;
                _seeded = true;
                return false;
            }

            double elapsedSeconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            double cpuDeltaSeconds = (cpu - _lastCpu).TotalSeconds;
            _lastCpu = cpu;
            _lastTimestamp = now;

            double? percent = CpuUtilizationCalculator.ComputeProcessPercent(cpuDeltaSeconds, elapsedSeconds, _processorCount);
            if (percent is null)
            {
                return false;
            }

            utilizationPercent = percent.Value;
            return true;
        }
    }
}
