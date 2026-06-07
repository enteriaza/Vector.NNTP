// <copyright file="WindowsHostCpuUsageSampler.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Windows GetSystemTimes host CPU sampler.

using System.Runtime.InteropServices;

namespace Vector.NNTP.Utilities.Metrics
{
    /// <summary>
    /// Samples machine-wide CPU busy percent on Windows using cumulative <c>GetSystemTimes</c> counters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements <see cref="ICpuUsageSignalSampler"/> for the <see cref="CpuUsageSignalNames.Host"/> signal.
    /// Each call reads idle, kernel, and user <see cref="FILETIME"/> values, converts them to 100-nanosecond ticks,
    /// and derives busy percent for the interval since the prior sample via <see cref="CpuUtilizationCalculator.ComputeHostPercent"/>.
    /// </para>
    /// <para>
    /// On Windows, kernel time reported by <c>GetSystemTimes</c> includes idle time; busy time is therefore
    /// <c>(kernelDelta + userDelta - idleDelta)</c> over <c>(kernelDelta + userDelta + idleDelta)</c>.
    /// </para>
    /// <para>
    /// The first successful read seeds internal baselines and returns <see langword="false"/> so the next interval
    /// has a defined delta window. Non-Windows hosts report <see cref="IsAvailable"/> as <see langword="false"/>.
    /// </para>
    /// </remarks>
    public sealed partial class WindowsHostCpuUsageSampler : ICpuUsageSignalSampler
    {
        /// <summary>
        /// Baseline idle-processor FILETIME ticks (100 ns units) from the previous sample.
        /// </summary>
        private long _lastIdleTicks;

        /// <summary>
        /// Baseline kernel-mode FILETIME ticks (100 ns units) from the previous sample.
        /// </summary>
        /// <remarks>Kernel time from <c>GetSystemTimes</c> includes idle time on Windows.</remarks>
        private long _lastKernelTicks;

        /// <summary>
        /// Baseline user-mode FILETIME ticks (100 ns units) from the previous sample.
        /// </summary>
        private long _lastUserTicks;

        /// <summary>
        /// Indicates whether baseline counters have been captured for delta calculation.
        /// </summary>
        private bool _seeded;

        /// <summary>
        /// Gets the stable host-wide signal name (<see cref="CpuUsageSignalNames.Host"/>).
        /// </summary>
        public string SignalName => CpuUsageSignalNames.Host;

        /// <summary>
        /// Gets a value indicating whether host-wide sampling is supported on this operating system.
        /// </summary>
        /// <remarks>Returns <see langword="true"/> only when <see cref="OperatingSystem.IsWindows()"/> is <see langword="true"/>.</remarks>
        public bool IsAvailable => OperatingSystem.IsWindows();

        /// <summary>
        /// Attempts to sample machine-wide CPU utilization for the interval since the prior call.
        /// </summary>
        /// <param name="utilizationPercent">
        /// When this method returns <see langword="true"/>, receives the raw busy percent in [0, 100]
        /// after clamping by <see cref="CpuUtilizationCalculator.ComputeHostPercent"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a new utilization sample was produced;
        /// <see langword="false"/> when the platform is unsupported, <c>GetSystemTimes</c> fails,
        /// the series is being seeded, or the elapsed counter delta is non-positive.
        /// </returns>
        public bool TrySample(out double utilizationPercent)
        {
            utilizationPercent = 0;
            if (!IsAvailable)
            {
                return false;
            }

            if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user))
            {
                return false;
            }

            long idleTicks = FileTimeToInt64(idle);
            long kernelTicks = FileTimeToInt64(kernel);
            long userTicks = FileTimeToInt64(user);

            if (!_seeded)
            {
                _lastIdleTicks = idleTicks;
                _lastKernelTicks = kernelTicks;
                _lastUserTicks = userTicks;
                _seeded = true;
                return false;
            }

            long idleDelta = idleTicks - _lastIdleTicks;
            long kernelDelta = kernelTicks - _lastKernelTicks;
            long userDelta = userTicks - _lastUserTicks;
            _lastIdleTicks = idleTicks;
            _lastKernelTicks = kernelTicks;
            _lastUserTicks = userTicks;

            long totalDelta = kernelDelta + userDelta + idleDelta;
            if (totalDelta <= 0)
            {
                return false;
            }

            long busyDelta = kernelDelta + userDelta - idleDelta;
            double busySeconds = busyDelta / 10_000_000.0;
            double totalSeconds = totalDelta / 10_000_000.0;
            double? percent = CpuUtilizationCalculator.ComputeHostPercent(busySeconds, totalSeconds);
            if (percent is null)
            {
                return false;
            }

            utilizationPercent = percent.Value;
            return true;
        }

        /// <summary>
        /// Combines a Win32 <see cref="FILETIME"/> low and high parts into a single 64-bit tick count.
        /// </summary>
        /// <param name="fileTime">100-nanosecond interval FILETIME returned by <see cref="GetSystemTimes"/>.</param>
        /// <returns>Signed 64-bit tick count suitable for delta subtraction between consecutive samples.</returns>
        private static long FileTimeToInt64(FILETIME fileTime)
        {
            return ((long)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
        }

        /// <summary>
        /// Retrieves system-wide idle, kernel, and user CPU time counters from the Windows kernel.
        /// </summary>
        /// <param name="idleTime">Receives cumulative idle-processor time.</param>
        /// <param name="kernelTime">Receives cumulative kernel-mode time (includes idle on Windows).</param>
        /// <param name="userTime">Receives cumulative user-mode time.</param>
        /// <returns>
        /// <see langword="true"/> when the call succeeds; otherwise <see langword="false"/> and the caller
        /// should consult Win32 last error if diagnostics are required.
        /// </returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        /// <summary>
        /// Blittable layout matching the Win32 <c>FILETIME</c> structure used by <see cref="GetSystemTimes"/>.
        /// </summary>
        /// <remarks>
        /// Field order is low DWORD then high DWORD per <see cref="StructLayoutAttribute"/> sequential marshalling.
        /// Each unit represents a 100-nanosecond interval.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            /// <summary>
            /// Low-order 32 bits of the 100-nanosecond interval count.
            /// </summary>
            internal uint dwLowDateTime;

            /// <summary>
            /// High-order 32 bits of the 100-nanosecond interval count.
            /// </summary>
            internal uint dwHighDateTime;
        }
    }
}
