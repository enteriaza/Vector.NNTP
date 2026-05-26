// <copyright file="RetryUtilitiesTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Vector.NNTP.MessageBus.Utilities;

namespace Vector.NNTP.Tests.MessageBus.Utilities;

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
}
