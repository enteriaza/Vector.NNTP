// <copyright file="LinuxCgroupCpuUsageSamplerTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Metrics;

namespace Vector.NNTP.Tests.Utilities.Metrics;

/// <summary>
/// Tests for <see cref="LinuxCgroupCpuUsageSampler"/>.
/// </summary>
[TestFixture]
public sealed class LinuxCgroupCpuUsageSamplerTests
{
    /// <summary>
    /// Verifies cgroup v2 quota-relative sampling.
    /// </summary>
    [Test]
    [Platform("Linux")]
    public void TrySample_V2QuotaRelativeUtilization()
    {
        string root = CreateV2Fixture(quotaUs: 50_000, periodUs: 100_000);
        try
        {
            var sampler = new LinuxCgroupCpuUsageSampler(
                () => "0::/test.slice/app\n",
                root);

            Assert.That(sampler.TrySample(out _), Is.False);
            Thread.Sleep(15);
            File.WriteAllText(Path.Combine(root, "test.slice", "app", "cpu.stat"), "usage_usec 500000\n");
            Assert.That(sampler.TrySample(out double percent), Is.True);
            Assert.That(percent, Is.GreaterThan(0));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies unlimited cgroup quota excludes sampler from gate.
    /// </summary>
    [Test]
    [Platform("Linux")]
    public void IsAvailable_FalseWhenQuotaUnlimited()
    {
        string root = CreateV2Fixture(quotaUs: -1, periodUs: 100_000, unlimited: true);
        try
        {
            var sampler = new LinuxCgroupCpuUsageSampler(
                () => "0::/test.slice/app\n",
                root);
            Assert.That(sampler.IsAvailable, Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Creates a temporary cgroup v2 fixture directory.
    /// </summary>
    /// <param name="quotaUs">Quota microseconds (ignored when unlimited).</param>
    /// <param name="periodUs">Period microseconds.</param>
    /// <param name="unlimited">Whether <c>cpu.max</c> is unlimited.</param>
    /// <returns>Fixture root path.</returns>
    private static string CreateV2Fixture(long quotaUs, long periodUs, bool unlimited = false)
    {
        string root = Path.Combine(Path.GetTempPath(), "cgroup-fixture-" + Guid.NewGuid().ToString("N"));
        string dir = Path.Combine(root, "test.slice", "app");
        Directory.CreateDirectory(dir);
        string cpuMax = unlimited ? "max 100000" : $"{quotaUs} {periodUs}";
        File.WriteAllText(Path.Combine(dir, "cpu.max"), cpuMax);
        File.WriteAllText(Path.Combine(dir, "cpu.stat"), "usage_usec 0\n");
        return root;
    }
}
