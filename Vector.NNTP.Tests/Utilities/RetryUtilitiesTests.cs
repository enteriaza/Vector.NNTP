// <copyright file="RetryUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.Utilities.Retry;

namespace Vector.NNTP.Tests.Utilities;

/// <summary>
/// Tests for <see cref="RetryUtilities"/>.
/// </summary>
[TestFixture]
public sealed class RetryUtilitiesTests
{
    /// <summary>
    /// Verifies jittered delay stays within bounds.
    /// </summary>
    [Test]
    public void CalculateBackOff_StaysWithinCap()
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            int delayMs = RetryUtilities.CalculateBackOff(attempt, baseDelayMs: 1000, maxDelayMs: 5000, jitterMaxMs: 500);
            Assert.That(delayMs, Is.InRange(1000, 5500));
        }
    }

    /// <summary>
    /// Verifies backoff increases monotonically when jitter is disabled.
    /// </summary>
    [Test]
    public void CalculateBackOff_IsMonotonicWithoutJitter()
    {
        int previous = RetryUtilities.CalculateBackOff(1, baseDelayMs: 250, maxDelayMs: 10_000, jitterMaxMs: 0);

        for (int attempt = 2; attempt <= 6; attempt++)
        {
            int current = RetryUtilities.CalculateBackOff(attempt, baseDelayMs: 250, maxDelayMs: 10_000, jitterMaxMs: 0);
            Assert.That(current, Is.GreaterThanOrEqualTo(previous));
            previous = current;
        }
    }
}
