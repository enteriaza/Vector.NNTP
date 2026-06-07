// <copyright file="LinuxHostCpuUsageSampler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Linux /proc/stat host CPU sampler.

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Samples machine-wide CPU busy percent on Linux from cumulative <c>/proc/stat</c> jiffies.
    /// </summary>
    /// <param name="readProcStat">
    /// Delegate that supplies <c>/proc/stat</c> text. Production code reads <c>/proc/stat</c>;
    /// tests inject synthetic excerpts.
    /// </param>
    /// <remarks>
    /// <para>
    /// Implements <see cref="ICpuUsageSignalSampler"/> for the <see cref="CpuUsageSignalNames.Host"/> signal.
    /// Each call reads aggregate busy and total jiffies via <see cref="ProcStatParser.TryParseAggregateCpuJiffies"/>
    /// and computes <c>busyDelta / totalDelta * 100</c> for the interval since the prior sample, then clamps
    /// with <see cref="CpuUtilizationCalculator.ClampPercent"/>.
    /// </para>
    /// <para>
    /// The first successful parse seeds internal baselines and returns <see langword="false"/>.
    /// I/O or permission failures from <paramref name="readProcStat"/> are treated as a failed sample
    /// (returns <see langword="false"/> without throwing).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="readProcStat"/> is <see langword="null"/>.
    /// </exception>
    public sealed class LinuxHostCpuUsageSampler(Func<string> readProcStat) : ICpuUsageSignalSampler
    {
        /// <summary>
        /// Validated reader delegate invoked on each sample attempt.
        /// </summary>
        private readonly Func<string> _readProcStat = readProcStat ?? throw new ArgumentNullException(nameof(readProcStat));

        /// <summary>
        /// Baseline aggregate busy jiffies from the previous sample.
        /// </summary>
        private ulong _lastBusyJiffies;

        /// <summary>
        /// Baseline aggregate total jiffies (busy plus idle and iowait) from the previous sample.
        /// </summary>
        private ulong _lastTotalJiffies;

        /// <summary>
        /// Indicates whether baseline jiffies have been captured for delta calculation.
        /// </summary>
        private bool _seeded;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinuxHostCpuUsageSampler"/> class that reads <c>/proc/stat</c>.
        /// </summary>
        public LinuxHostCpuUsageSampler()
            : this(static () => File.ReadAllText("/proc/stat"))
        {
        }

        /// <summary>
        /// Gets the stable host-wide signal name (<see cref="CpuUsageSignalNames.Host"/>).
        /// </summary>
        public string SignalName => CpuUsageSignalNames.Host;

        /// <summary>
        /// Gets a value indicating whether host-wide sampling is supported on this operating system.
        /// </summary>
        /// <remarks>Returns <see langword="true"/> only when <see cref="OperatingSystem.IsLinux()"/> is <see langword="true"/>.</remarks>
        public bool IsAvailable => OperatingSystem.IsLinux();

        /// <summary>
        /// Attempts to sample machine-wide CPU utilization for the interval since the prior call.
        /// </summary>
        /// <param name="utilizationPercent">
        /// When this method returns <see langword="true"/>, receives the raw busy percent in [0, 100]
        /// after clamping by <see cref="CpuUtilizationCalculator.ClampPercent"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a new utilization sample was produced;
        /// <see langword="false"/> when the platform is unsupported, <c>/proc/stat</c> cannot be read or parsed,
        /// the series is being seeded, or the jiffies total delta is zero.
        /// </returns>
        public bool TrySample(out double utilizationPercent)
        {
            utilizationPercent = 0;
            if (!IsAvailable)
            {
                return false;
            }

            string content;
            try
            {
                content = _readProcStat();
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (!ProcStatParser.TryParseAggregateCpuJiffies(content, out ulong busy, out ulong total))
            {
                return false;
            }

            if (!_seeded)
            {
                _lastBusyJiffies = busy;
                _lastTotalJiffies = total;
                _seeded = true;
                return false;
            }

            ulong busyDelta = busy - _lastBusyJiffies;
            ulong totalDelta = total - _lastTotalJiffies;
            _lastBusyJiffies = busy;
            _lastTotalJiffies = total;

            if (totalDelta == 0)
            {
                return false;
            }

            double percent = (double)busyDelta / totalDelta * 100.0;
            utilizationPercent = CpuUtilizationCalculator.ClampPercent(percent);
            return true;
        }
    }
}
