// <copyright file="CpuUtilizationCalculatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Tests.Utilities.Metrics;

/// <summary>
/// Tests for <see cref="CpuUtilizationCalculator"/>.
/// </summary>
[TestFixture]
public sealed class CpuUtilizationCalculatorTests
{
    /// <summary>
    /// Verifies process percent formula.
    /// </summary>
    [Test]
    public void ComputeProcessPercent_ReturnsExpectedValue()
    {
        double? percent = CpuUtilizationCalculator.ComputeProcessPercent(0.8, 1.0, 4);
        Assert.That(percent, Is.EqualTo(20).Within(0.001));
    }

    /// <summary>
    /// Verifies host percent formula.
    /// </summary>
    [Test]
    public void ComputeHostPercent_ReturnsExpectedValue()
    {
        double? percent = CpuUtilizationCalculator.ComputeHostPercent(0.75, 1.0);
        Assert.That(percent, Is.EqualTo(75).Within(0.001));
    }

    /// <summary>
    /// Verifies cgroup quota-relative percent formula.
    /// </summary>
    [Test]
    public void ComputeCgroupPercent_ReturnsExpectedValue()
    {
        double? percent = CpuUtilizationCalculator.ComputeCgroupPercent(0.5, 1.0, 0.5);
        Assert.That(percent, Is.EqualTo(100).Within(0.001));
    }

    /// <summary>
    /// Verifies zero elapsed returns null.
    /// </summary>
    [Test]
    public void ComputeProcessPercent_ZeroElapsed_ReturnsNull()
    {
        Assert.That(CpuUtilizationCalculator.ComputeProcessPercent(1, 0, 4), Is.Null);
    }

    /// <summary>
    /// Verifies clamping to [0, 100].
    /// </summary>
    [Test]
    public void ClampPercent_ClampsHighAndLow()
    {
        Assert.That(CpuUtilizationCalculator.ClampPercent(-5), Is.EqualTo(0));
        Assert.That(CpuUtilizationCalculator.ClampPercent(150), Is.EqualTo(100));
        Assert.That(CpuUtilizationCalculator.ClampPercent(42), Is.EqualTo(42));
    }
}
