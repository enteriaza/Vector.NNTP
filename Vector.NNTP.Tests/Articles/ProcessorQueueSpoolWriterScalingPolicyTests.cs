// <copyright file="ProcessorQueueSpoolWriterScalingPolicyTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Articles.Storage;

namespace Vector.NNTP.Tests.Articles;

/// <summary>
/// Tests for <see cref="ProcessorQueueSpoolWriterScalingPolicy"/>.
/// </summary>
[TestFixture]
public sealed class ProcessorQueueSpoolWriterScalingPolicyTests
{
    /// <summary>
    /// Verifies zero queue depth keeps one baseline writer.
    /// </summary>
    [Test]
    public void ComputeDesiredWriters_ZeroDepth_ReturnsMinWriters()
    {
        ProcessorQueueSpoolWriterScalingPolicy policy = new ProcessorQueueSpoolWriterScalingPolicy();
        Assert.That(policy.ComputeDesiredWriters(0, 1024), Is.EqualTo(policy.MinWriters));
    }

    /// <summary>
    /// Verifies backlog at the hard-cap tier requests the policy maximum writer count.
    /// </summary>
    [Test]
    public void ComputeDesiredWriters_BacklogAtHardCapTier_ReturnsMaxWriters()
    {
        ProcessorQueueSpoolWriterScalingPolicy policy = new ProcessorQueueSpoolWriterScalingPolicy();
        long depthForMax = ProcessorQueueSpoolWriterScalingPolicy.BacklogPerWriter * policy.MaxWriters;
        Assert.That(policy.ComputeDesiredWriters(depthForMax, 100_000), Is.EqualTo(policy.MaxWriters));
    }

    /// <summary>
    /// Verifies shallow backlog stays at one writer instead of ramping on low occupancy percentage.
    /// </summary>
    [Test]
    public void ComputeDesiredWriters_ShallowBacklog_StaysAtMinWriters()
    {
        ProcessorQueueSpoolWriterScalingPolicy policy = new ProcessorQueueSpoolWriterScalingPolicy();
        Assert.That(policy.ComputeDesiredWriters(11, 1024), Is.EqualTo(policy.MinWriters));
    }

    /// <summary>
    /// Verifies bucket boundaries ramp writers only after each fixed backlog tier fills.
    /// </summary>
    [Test]
    public void ComputeDesiredWriters_BucketBoundaries_RampByAbsoluteDepth()
    {
        ProcessorQueueSpoolWriterScalingPolicy policy = new ProcessorQueueSpoolWriterScalingPolicy();
        const int capacity = 1024;
        int bucketSize = ProcessorQueueSpoolWriterScalingPolicy.BacklogPerWriter;

        Assert.That(policy.ComputeDesiredWriters(bucketSize, capacity), Is.EqualTo(1));
        Assert.That(policy.ComputeDesiredWriters(bucketSize + 1, capacity), Is.EqualTo(2));
        Assert.That(policy.ComputeDesiredWriters((2 * bucketSize) + 1, capacity), Is.EqualTo(3));
    }

    /// <summary>
    /// Verifies scaling is independent of configured queue capacity for the same backlog depth.
    /// </summary>
    [Test]
    public void ComputeDesiredWriters_SameDepthDifferentCapacities_ReturnsSameWriterCount()
    {
        ProcessorQueueSpoolWriterScalingPolicy policy = new ProcessorQueueSpoolWriterScalingPolicy();
        const long depth = 300;
        const int expectedWriters = 5;

        Assert.That(policy.ComputeDesiredWriters(depth, 1024), Is.EqualTo(expectedWriters));
        Assert.That(policy.ComputeDesiredWriters(depth, 8192), Is.EqualTo(expectedWriters));
        Assert.That(policy.ComputeDesiredWriters(depth, 100_000), Is.EqualTo(expectedWriters));
    }

    /// <summary>
    /// Verifies positive depth still scales when capacity is invalid because capacity is not used in the calculation.
    /// </summary>
    [Test]
    public void ComputeDesiredWriters_InvalidCapacityPositiveDepth_ScalesFromDepthOnly()
    {
        ProcessorQueueSpoolWriterScalingPolicy policy = new ProcessorQueueSpoolWriterScalingPolicy();
        const long depth = 1000;
        const int expectedWriters = 16;

        Assert.That(policy.ComputeDesiredWriters(depth, -1), Is.EqualTo(expectedWriters));
        Assert.That(policy.ComputeDesiredWriters(depth, 0), Is.EqualTo(expectedWriters));
    }
}
