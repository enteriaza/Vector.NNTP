// <copyright file="LinuxCgroupCpuUsageSampler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Linux cgroup v1/v2 quota-relative CPU sampler.

using System.Diagnostics;

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Samples process cgroup CPU utilization relative to cgroup CPU quota on Linux.
    /// </summary>
    /// <param name="readProcSelfCgroup">
    /// Delegate that supplies <c>/proc/self/cgroup</c> text. Production code reads the default path;
    /// tests inject synthetic cgroup membership lines.
    /// </param>
    /// <param name="cgroupRoot">
    /// Cgroup filesystem root used with paths from <paramref name="readProcSelfCgroup"/>
    /// (typically <see cref="CgroupPathResolver.DefaultCgroupRoot"/>).
    /// </param>
    /// <remarks>
    /// <para>
    /// Implements <see cref="ICpuUsageSignalSampler"/> for the <see cref="CpuUsageSignalNames.Cgroup"/> signal.
    /// On first use the sampler resolves the process cgroup directory via <see cref="CgroupPathResolver"/>,
    /// detects cgroup v1 vs v2, reads quota from <c>cpu.max</c> (v2) or <c>cpu.cfs_quota_us</c>/<c>cpu.cfs_period_us</c> (v1),
    /// and samples usage from <c>cpu.stat</c> (v2 <c>usage_usec</c>) or <c>cpuacct.usage</c> (v1 nanoseconds).
    /// </para>
    /// <para>
    /// Utilization is computed with <see cref="CpuUtilizationCalculator.ComputeCgroupPercent"/> as
    /// <c>usageDelta / (elapsed * quotaCores) * 100</c>, where <c>quotaCores = quota / period</c>.
    /// The first successful usage read seeds baselines and returns <see langword="false"/>.
    /// </para>
    /// <para>
    /// When cgroup quota is unlimited (<c>cpu.max max</c> or non-positive <c>cpu.cfs_quota_us</c>),
    /// <see cref="IsAvailable"/> is <see langword="false"/> and the signal is excluded from overload gating.
    /// Non-Linux hosts and processes outside a resolvable cgroup also report unavailable.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="readProcSelfCgroup"/> or <paramref name="cgroupRoot"/> is <see langword="null"/>.
    /// </exception>
    public sealed class LinuxCgroupCpuUsageSampler(Func<string> readProcSelfCgroup, string cgroupRoot) : ICpuUsageSignalSampler
    {
        /// <summary>
        /// Validated reader for <c>/proc/self/cgroup</c> invoked during lazy initialization.
        /// </summary>
        private readonly Func<string> _readProcSelfCgroup = readProcSelfCgroup ?? throw new ArgumentNullException(nameof(readProcSelfCgroup));

        /// <summary>
        /// Cgroup mount root combined with relative paths from the cgroup file.
        /// </summary>
        private readonly string _cgroupRoot = cgroupRoot ?? throw new ArgumentNullException(nameof(cgroupRoot));

        /// <summary>
        /// Resolved absolute cgroup directory for the current process, when initialization succeeded.
        /// </summary>
        private string? _cgroupDirectory;

        /// <summary>
        /// Whether the resolved hierarchy uses cgroup v2 unified files (<c>cpu.max</c>, <c>cpu.stat</c>).
        /// </summary>
        private bool _isV2;

        /// <summary>
        /// Whether a finite positive CPU quota was read from cgroup control files.
        /// </summary>
        private bool _hasFiniteQuota;

        /// <summary>
        /// Effective CPU cores allowed by cgroup quota (<c>quota / period</c>).
        /// </summary>
        private double _quotaCores;

        /// <summary>
        /// Baseline cgroup CPU usage counter (microseconds for v2, nanoseconds for v1) from the previous sample.
        /// </summary>
        private ulong _lastUsage;

        /// <summary>
        /// Baseline <see cref="Stopwatch.GetTimestamp"/> tick count from the previous sample.
        /// </summary>
        private long _lastTimestamp;

        /// <summary>
        /// Whether cgroup path and quota discovery has completed (successfully or not).
        /// </summary>
        private bool _initialized;

        /// <summary>
        /// Whether baseline usage and timestamp values have been captured.
        /// </summary>
        private bool _seeded;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinuxCgroupCpuUsageSampler"/> class using default Linux paths.
        /// </summary>
        public LinuxCgroupCpuUsageSampler()
            : this(static () => File.ReadAllText("/proc/self/cgroup"), CgroupPathResolver.DefaultCgroupRoot)
        {
        }

        /// <summary>
        /// Gets the stable cgroup quota signal name (<see cref="CpuUsageSignalNames.Cgroup"/>).
        /// </summary>
        public string SignalName => CpuUsageSignalNames.Cgroup;

        /// <summary>
        /// Gets a value indicating whether cgroup quota-relative sampling can contribute to overload gating.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="true"/> only on Linux when lazy initialization resolves a cgroup directory
        /// and a finite positive quota is present.
        /// </remarks>
        public bool IsAvailable => OperatingSystem.IsLinux() && EnsureInitialized() && _hasFiniteQuota;

        /// <summary>
        /// Attempts to sample cgroup quota-relative CPU utilization for the interval since the prior call.
        /// </summary>
        /// <param name="utilizationPercent">
        /// When this method returns <see langword="true"/>, receives the raw utilization percent in [0, 100]
        /// after clamping by <see cref="CpuUtilizationCalculator.ComputeCgroupPercent"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a new utilization sample was produced;
        /// <see langword="false"/> when the platform is unsupported, cgroup path or quota cannot be resolved,
        /// usage files are unreadable, the series is being seeded, or calculator inputs are invalid.
        /// </returns>
        public bool TrySample(out double utilizationPercent)
        {
            utilizationPercent = 0;
            if (!OperatingSystem.IsLinux())
            {
                return false;
            }

            if (!EnsureInitialized())
            {
                return false;
            }

            if (!_hasFiniteQuota || _cgroupDirectory is null)
            {
                return false;
            }

            ulong usage = ReadUsageCounter();
            long now = Stopwatch.GetTimestamp();

            if (!_seeded)
            {
                _lastUsage = usage;
                _lastTimestamp = now;
                _seeded = true;
                return false;
            }

            double elapsedSeconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            double usageDeltaSeconds = GetUsageDeltaSeconds(usage - _lastUsage);
            _lastUsage = usage;
            _lastTimestamp = now;

            double? percent = CpuUtilizationCalculator.ComputeCgroupPercent(usageDeltaSeconds, elapsedSeconds, _quotaCores);
            if (percent is null)
            {
                return false;
            }

            utilizationPercent = percent.Value;
            return true;
        }

        /// <summary>
        /// Performs one-time cgroup path and quota discovery when not already initialized.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when <see cref="_cgroupDirectory"/> was resolved;
        /// <see langword="false"/> when not in a cgroup, files are missing, or I/O fails.
        /// </returns>
        private bool EnsureInitialized()
        {
            if (_initialized)
            {
                return _cgroupDirectory is not null;
            }

            try
            {
                string content = _readProcSelfCgroup();
                if (!CgroupPathResolver.TryResolveFromCgroupFile(content, _cgroupRoot, out string? dir, out bool isV2))
                {
                    _initialized = true;
                    return false;
                }

                _cgroupDirectory = dir;
                _isV2 = isV2;
                _hasFiniteQuota = TryReadQuotaCores(out _quotaCores);
                _initialized = true;
                return _cgroupDirectory is not null;
            }
            catch (IOException)
            {
                _initialized = true;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                _initialized = true;
                return false;
            }
        }

        /// <summary>
        /// Reads cgroup CPU quota and converts it to effective core count (<c>quota / period</c>).
        /// </summary>
        /// <param name="quotaCores">
        /// When this method returns <see langword="true"/>, receives effective allowed CPU cores.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a finite positive quota was parsed from v1 or v2 control files;
        /// <see langword="false"/> for unlimited quota, missing files, or parse errors.
        /// </returns>
        private bool TryReadQuotaCores(out double quotaCores)
        {
            quotaCores = 0;
            if (_cgroupDirectory is null)
            {
                return false;
            }

            if (_isV2)
            {
                string cpuMaxPath = Path.Combine(_cgroupDirectory, "cpu.max");
                if (!File.Exists(cpuMaxPath))
                {
                    return false;
                }

                string[] parts = File.ReadAllText(cpuMaxPath).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0].Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!long.TryParse(parts[0], out long quotaUs) || !long.TryParse(parts[1], out long periodUs) || periodUs <= 0)
                {
                    return false;
                }

                quotaCores = quotaUs / (double)periodUs;
                return quotaCores > 0;
            }

            string quotaPath = Path.Combine(_cgroupDirectory, "cpu.cfs_quota_us");
            string periodPath = Path.Combine(_cgroupDirectory, "cpu.cfs_period_us");
            if (!File.Exists(quotaPath) || !File.Exists(periodPath))
            {
                return false;
            }

            if (!long.TryParse(File.ReadAllText(quotaPath).Trim(), out long cfsQuota) || cfsQuota <= 0)
            {
                return false;
            }

            if (!long.TryParse(File.ReadAllText(periodPath).Trim(), out long cfsPeriod) || cfsPeriod <= 0)
            {
                return false;
            }

            quotaCores = cfsQuota / (double)cfsPeriod;
            return quotaCores > 0;
        }

        /// <summary>
        /// Reads the cumulative cgroup CPU usage counter for the resolved hierarchy version.
        /// </summary>
        /// <returns>
        /// v2 <c>usage_usec</c> from <c>cpu.stat</c>, or v1 nanoseconds from <c>cpuacct.usage</c>;
        /// <c>0</c> when files are missing or lines cannot be parsed.
        /// </returns>
        private ulong ReadUsageCounter()
        {
            if (_cgroupDirectory is null)
            {
                return 0;
            }

            if (_isV2)
            {
                string statPath = Path.Combine(_cgroupDirectory, "cpu.stat");
                foreach (string line in File.ReadAllLines(statPath))
                {
                    if (line.StartsWith("usage_usec ", StringComparison.Ordinal))
                    {
                        return ulong.TryParse(line["usage_usec ".Length..].Trim(), out ulong usec) ? usec : 0;
                    }
                }

                return 0;
            }

            string usagePath = Path.Combine(_cgroupDirectory, "cpuacct.usage");
            return ulong.TryParse(File.ReadAllText(usagePath).Trim(), out ulong ns) ? ns : 0;
        }

        /// <summary>
        /// Converts a raw usage counter delta to seconds for the active cgroup hierarchy version.
        /// </summary>
        /// <param name="delta">Usage counter delta since the prior sample.</param>
        /// <returns>Elapsed CPU usage in seconds (microseconds for v2, nanoseconds for v1).</returns>
        private double GetUsageDeltaSeconds(ulong delta)
        {
            return _isV2 ? delta / 1_000_000.0 : delta / 1_000_000_000.0;
        }
    }
}
