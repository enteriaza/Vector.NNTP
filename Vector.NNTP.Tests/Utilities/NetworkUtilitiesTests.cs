// <copyright file="NetworkUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Retry;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="RetryUtilities.CalculateBackOff"/>.
/// </summary>
[TestFixture]
public sealed class NetworkUtilitiesTests
{
    /// <summary>
    /// Verifies exponential back-off is capped and non-negative without jitter.
    /// </summary>
    [Test]
    public void CalculateBackOff_WithoutJitter_CapsAtMaximum()
    {
        Assert.That(RetryUtilities.CalculateBackOff(1, 30_000, 300_000, jitterMaxMs: 0), Is.EqualTo(30_000));
        Assert.That(RetryUtilities.CalculateBackOff(2, 30_000, 300_000, jitterMaxMs: 0), Is.EqualTo(60_000));
        Assert.That(RetryUtilities.CalculateBackOff(4, 30_000, 300_000, jitterMaxMs: 0), Is.EqualTo(240_000));
        Assert.That(RetryUtilities.CalculateBackOff(5, 30_000, 300_000, jitterMaxMs: 0), Is.EqualTo(300_000));
    }

    /// <summary>
    /// Verifies attempt values below one behave like the first attempt.
    /// </summary>
    [Test]
    public void CalculateBackOff_ClampAttemptToMinimumOne()
    {
        Assert.That(
            RetryUtilities.CalculateBackOff(0, 1_000, 5_000, jitterMaxMs: 0),
            Is.EqualTo(RetryUtilities.CalculateBackOff(1, 1_000, 5_000, jitterMaxMs: 0)));
    }

    /// <summary>
    /// Verifies jitter stays within the configured window above the capped delay.
    /// </summary>
    [Test]
    public void CalculateBackOff_WithJitter_StaysWithinBounds()
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            int delayMs = RetryUtilities.CalculateBackOff(attempt, baseDelayMs: 1_000, maxDelayMs: 5_000, jitterMaxMs: 500);
            Assert.That(delayMs, Is.InRange(1_000, 5_499));
        }
    }
}
