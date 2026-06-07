// <copyright file="NntpCpuLoadMonitorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Options;
using Vector.NNTP.Sockets.Configuration;
using Vector.NNTP.Sockets.Metrics;
using Vector.NNTP.Tests.Sockets.Fakes;
using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Tests.Sockets;

/// <summary>
/// Tests for <see cref="NntpCpuLoadMonitor"/> EWMA blending and hysteresis gate.
/// </summary>
[TestFixture]
public sealed class NntpCpuLoadMonitorTests
{
    /// <summary>
    /// Verifies hysteresis boundaries at reject and resume thresholds.
    /// </summary>
    [Test]
    public void Gate_HysteresisAtThresholdBoundaries()
    {
        var options = Options.Create(new NntpServerOptions
        {
            NodeName = "n1",
            ServerIdentification = "test",
            CpuRejectEnabled = true,
            CpuRejectThresholdPercent = 80,
            CpuResumeThresholdPercent = 75,
            CpuRejectUseProcess = true,
            CpuRejectUseHost = false,
            CpuRejectUseCgroup = false,
        });
        var monitor = new OptionsMonitorStub<NntpServerOptions>(options.Value);
        var sampler = new FakeCpuUsageSignalSampler(CpuUsageSignalNames.Process, 79.99);
        var subject = new NntpCpuLoadMonitor(monitor, new[] { sampler });

        subject.RecordSample();
        subject.RecordSample();
        Assert.That(subject.IsOverloaded(), Is.False);

        sampler.SamplePercent = 85.0;
        for (int i = 0; i < 5; i++)
        {
            subject.RecordSample();
        }

        Assert.That(subject.IsOverloaded(), Is.True);

        sampler.SamplePercent = 70.0;
        for (int i = 0; i < 10; i++)
        {
            subject.RecordSample();
        }

        Assert.That(subject.IsOverloaded(), Is.False);
    }

    /// <summary>
    /// Verifies effective utilization uses maximum enabled source EWMA.
    /// </summary>
    [Test]
    public void Effective_UsesMaxEnabledSource()
    {
        var options = Options.Create(new NntpServerOptions
        {
            NodeName = "n1",
            ServerIdentification = "test",
            CpuRejectUseProcess = true,
            CpuRejectUseHost = true,
            CpuRejectUseCgroup = false,
        });
        var monitor = new OptionsMonitorStub<NntpServerOptions>(options.Value);
        var samplers = new ICpuUsageSignalSampler[]
        {
            new FakeCpuUsageSignalSampler(CpuUsageSignalNames.Process, 40),
            new FakeCpuUsageSignalSampler(CpuUsageSignalNames.Host, 90),
        };
        var subject = new NntpCpuLoadMonitor(monitor, samplers);
        subject.RecordSample();
        subject.RecordSample();
        NntpCpuLoadSnapshot snap = subject.GetSnapshot();
        Assert.That(snap.DominantSignal, Is.EqualTo(CpuUsageSignalNames.Host));
        Assert.That(snap.EffectiveEwmaPercent, Is.EqualTo(90).Within(0.1));
    }

    /// <summary>
    /// Fixed-value <see cref="IOptionsMonitor{TOptions}"/> for unit tests.
    /// </summary>
    /// <typeparam name="T">Options type.</typeparam>
    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
        where T : class
    {
        private readonly T _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsMonitorStub{T}"/> class.
        /// </summary>
        /// <param name="value">Fixed options value.</param>
        public OptionsMonitorStub(T value) => _value = value;

        /// <inheritdoc />
        public T CurrentValue => _value;

        /// <inheritdoc />
        public T Get(string? name) => _value;

        /// <inheritdoc />
        public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();

        /// <summary>
        /// No-op change subscription token.
        /// </summary>
        private sealed class NoopDisposable : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
            }
        }
    }
}
