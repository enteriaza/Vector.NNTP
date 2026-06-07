// <copyright file="FakeNntpCpuLoadMonitor.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: test double for CPU overload gate.

using Vector.NNTP.Sockets.Metrics;
using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Tests.Sockets.Fakes
{
    /// <summary>
    /// Configurable <see cref="INntpCpuLoadMonitor"/> for protocol harness tests.
    /// </summary>
    internal sealed class FakeNntpCpuLoadMonitor : INntpCpuLoadMonitor
    {
        /// <summary>
        /// Gets or sets a value indicating whether the gate is rejecting.
        /// </summary>
        public bool Overloaded { get; set; }

        /// <inheritdoc />
        public bool IsOverloaded() => this.Overloaded;

        /// <inheritdoc />
        public NntpCpuLoadSnapshot GetSnapshot() =>
            new(
                ProcessEwmaPercent: 90,
                HostEwmaPercent: null,
                CgroupEwmaPercent: null,
                EffectiveEwmaPercent: 90,
                DominantSignal: CpuUsageSignalNames.Process,
                GateState: this.Overloaded ? "rejecting" : "accepting",
                RejectThresholdPercent: 80,
                ResumeThresholdPercent: 75);

        /// <inheritdoc />
        public void RecordSample()
        {
        }
    }
}
