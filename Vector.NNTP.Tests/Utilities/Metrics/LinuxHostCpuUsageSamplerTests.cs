// <copyright file="LinuxHostCpuUsageSamplerTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Tests.Utilities.Metrics;

/// <summary>
/// Tests for <see cref="LinuxHostCpuUsageSampler"/>.
/// </summary>
[TestFixture]
public sealed class LinuxHostCpuUsageSamplerTests
{
    /// <summary>
    /// Verifies jiffies delta produces host busy percent.
    /// </summary>
    [Test]
    public void TrySample_ComputesBusyPercentFromDeltas()
    {
        int call = 0;
        var sampler = new LinuxHostCpuUsageSampler(() =>
        {
            call++;
            return call == 1
                ? "cpu  100 0 0 900 0 0 0 0 0 0\n"
                : "cpu  200 0 0 800 0 0 0 0 0 0\n";
        });

        Assert.That(sampler.TrySample(out _), Is.False);
        Assert.That(sampler.TrySample(out double percent), Is.True);
        Assert.That(percent, Is.EqualTo(50).Within(0.01));
    }
}
