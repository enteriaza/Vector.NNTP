// <copyright file="NntpCpuLoadMonitor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: EWMA CPU utilization monitor with Volatile hysteresis overload gate.

using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Sockets.Metrics
{
    /// <summary>
    /// Blends per-source CPU utilization EWMAs and drives a hysteresis overload gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Effective utilization is <c>Max(enabled, available source EWMAs)</c>. Hysteresis applies to the effective EWMA.
    /// </para>
    /// <para>
    /// Hot-path readers use <see cref="IsOverloaded"/> only; sampling runs on a background timer.
    /// </para>
    /// </remarks>
    public sealed class NntpCpuLoadMonitor : INntpCpuLoadMonitor
    {
        /// <summary>
        /// Monitored server options supplying thresholds, signal flags, and sampling enablement.
        /// </summary>
        private readonly IOptionsMonitor<NntpServerOptions> _options;

        /// <summary>
        /// Platform CPU signal samplers blended into the effective EWMA.
        /// </summary>
        private readonly IReadOnlyList<ICpuUsageSignalSampler> _samplers;

        /// <summary>
        /// EWMA bits for the process CPU signal.
        /// </summary>
        private long _processEwmaBits;

        /// <summary>
        /// EWMA bits for the host-wide CPU signal.
        /// </summary>
        private long _hostEwmaBits;

        /// <summary>
        /// EWMA bits for the cgroup quota-relative CPU signal.
        /// </summary>
        private long _cgroupEwmaBits;

        /// <summary>
        /// EWMA bits for the max-of-sources effective utilization.
        /// </summary>
        private long _effectiveEwmaBits;

        /// <summary>
        /// Non-zero after the process EWMA has been seeded.
        /// </summary>
        private int _processSeeded;

        /// <summary>
        /// Non-zero after the host EWMA has been seeded.
        /// </summary>
        private int _hostSeeded;

        /// <summary>
        /// Non-zero after the cgroup EWMA has been seeded.
        /// </summary>
        private int _cgroupSeeded;

        /// <summary>
        /// Non-zero after the effective EWMA has been seeded.
        /// </summary>
        private int _effectiveSeeded;

        /// <summary>
        /// Hysteresis gate flag (non-zero = rejecting new work).
        /// </summary>
        private int _overloaded;

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpCpuLoadMonitor"/> class with platform samplers.
        /// </summary>
        /// <param name="options">Server options supplying thresholds and signal flags.</param>
        public NntpCpuLoadMonitor(IOptionsMonitor<NntpServerOptions> options)
            : this(options, CpuUsageSignalSamplerFactory.CreateDefault())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NntpCpuLoadMonitor"/> class with explicit samplers (tests).
        /// </summary>
        /// <param name="options">Server options supplying thresholds and signal flags.</param>
        /// <param name="samplers">CPU signal samplers.</param>
        public NntpCpuLoadMonitor(IOptionsMonitor<NntpServerOptions> options, IReadOnlyList<ICpuUsageSignalSampler> samplers)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _samplers = samplers ?? throw new ArgumentNullException(nameof(samplers));
        }

        /// <summary>
        /// Gets a value indicating whether the gate is in the rejecting state.
        /// </summary>
        /// <returns><see langword="true"/> when new connections and commands should receive <c>400</c> and close.</returns>
        public bool IsOverloaded()
        {
            NntpServerOptions opts = _options.CurrentValue;
            if (!opts.CpuRejectEnabled)
            {
                return false;
            }

            return Volatile.Read(ref _overloaded) != 0;
        }

        /// <summary>
        /// Captures the current EWMA snapshot for structured reject logging and metrics.
        /// </summary>
        /// <returns>Point-in-time utilization and gate state.</returns>
        public NntpCpuLoadSnapshot GetSnapshot()
        {
            NntpServerOptions opts = _options.CurrentValue;
            double? process = ReadSourceEwma(CpuUsageSignalNames.Process, _processEwmaBits, _processSeeded, opts.CpuRejectUseProcess);
            double? host = ReadSourceEwma(CpuUsageSignalNames.Host, _hostEwmaBits, _hostSeeded, opts.CpuRejectUseHost);
            double? cgroup = ReadSourceEwma(CpuUsageSignalNames.Cgroup, _cgroupEwmaBits, _cgroupSeeded, opts.CpuRejectUseCgroup);
            double effective = EwmaUtilities.AtomicRead(ref _effectiveEwmaBits);
            string dominant = ResolveDominantSignal(process, host, cgroup);
            string gateState = Volatile.Read(ref _overloaded) != 0 ? "rejecting" : "accepting";
            return new NntpCpuLoadSnapshot(
                process,
                host,
                cgroup,
                effective,
                dominant,
                gateState,
                opts.CpuRejectThresholdPercent,
                opts.CpuResumeThresholdPercent);
        }

        /// <summary>
        /// Samples enabled CPU signals, blends EWMAs, and updates the hysteresis gate.
        /// </summary>
        /// <remarks>Called periodically from <see cref="Hosting.NntpCpuLoadSamplerHostedService"/>.</remarks>
        public void RecordSample()
        {
            NntpServerOptions opts = _options.CurrentValue;
            if (!opts.CpuRejectEnabled)
            {
                Volatile.Write(ref _overloaded, 0);
                return;
            }

            bool anyEnabledSample = false;
            double maxEwma = 0;
            string dominant = CpuUsageSignalNames.Process;

            foreach (ICpuUsageSignalSampler sampler in _samplers)
            {
                if (!IsSignalEnabled(opts, sampler.SignalName))
                {
                    continue;
                }

                if (!sampler.IsAvailable || !sampler.TrySample(out double rawPercent))
                {
                    continue;
                }

                double clamped = CpuUtilizationCalculator.ClampPercent(rawPercent);
                double ewma = BlendSourceEwma(sampler.SignalName, clamped);
                anyEnabledSample = true;
                if (ewma >= maxEwma)
                {
                    maxEwma = ewma;
                    dominant = sampler.SignalName;
                }
            }

            if (!anyEnabledSample)
            {
                return;
            }

            double priorEffective = EwmaUtilities.AtomicRead(ref _effectiveEwmaBits);
            bool hasEffective = Volatile.Read(ref _effectiveSeeded) != 0;
            double effective = EwmaUtilities.BlendOrSeed(priorEffective, maxEwma, hasEffective);
            EwmaUtilities.AtomicWrite(ref _effectiveEwmaBits, effective);
            if (!hasEffective)
            {
                Volatile.Write(ref _effectiveSeeded, 1);
            }

            UpdateGate(effective, opts);
            NntpServerLoadMetrics.RecordCpuEwma(effective, GetSnapshot());
        }

        private void UpdateGate(double effectiveCpu, NntpServerOptions opts)
        {
            int overloaded = Volatile.Read(ref _overloaded);
            if (overloaded == 0 && effectiveCpu >= opts.CpuRejectThresholdPercent)
            {
                Volatile.Write(ref _overloaded, 1);
            }
            else if (overloaded != 0 && effectiveCpu <= opts.CpuResumeThresholdPercent)
            {
                Volatile.Write(ref _overloaded, 0);
            }

            NntpServerLoadMetrics.RecordGateState(Volatile.Read(ref _overloaded) != 0);
        }

        private double BlendSourceEwma(string signalName, double sample)
        {
            switch (signalName)
            {
                case CpuUsageSignalNames.Process:
                    return BlendInto(ref _processEwmaBits, ref _processSeeded, sample);
                case CpuUsageSignalNames.Host:
                    return BlendInto(ref _hostEwmaBits, ref _hostSeeded, sample);
                case CpuUsageSignalNames.Cgroup:
                    return BlendInto(ref _cgroupEwmaBits, ref _cgroupSeeded, sample);
                default:
                    return sample;
            }
        }

        private static double BlendInto(ref long bits, ref int seeded, double sample)
        {
            bool hasValue = Volatile.Read(ref seeded) != 0;
            double old = EwmaUtilities.AtomicRead(ref bits);
            double blended = EwmaUtilities.BlendOrSeed(old, sample, hasValue);
            EwmaUtilities.AtomicWrite(ref bits, blended);
            if (!hasValue)
            {
                Volatile.Write(ref seeded, 1);
            }

            return blended;
        }

        private static bool IsSignalEnabled(NntpServerOptions opts, string signalName)
        {
            return signalName switch
            {
                CpuUsageSignalNames.Process => opts.CpuRejectUseProcess,
                CpuUsageSignalNames.Host => opts.CpuRejectUseHost,
                CpuUsageSignalNames.Cgroup => opts.CpuRejectUseCgroup,
                _ => false,
            };
        }

        private static double? ReadSourceEwma(string signalName, long bits, int seeded, bool enabled)
        {
            if (!enabled || Volatile.Read(ref seeded) == 0)
            {
                return null;
            }

            _ = signalName;
            return EwmaUtilities.AtomicRead(ref bits);
        }

        private static string ResolveDominantSignal(double? process, double? host, double? cgroup)
        {
            double max = -1;
            string dominant = CpuUsageSignalNames.Process;
            if (process is double p && p >= max)
            {
                max = p;
                dominant = CpuUsageSignalNames.Process;
            }

            if (host is double h && h > max)
            {
                max = h;
                dominant = CpuUsageSignalNames.Host;
            }

            if (cgroup is double c && c > max)
            {
                dominant = CpuUsageSignalNames.Cgroup;
            }

            return dominant;
        }
    }
}
