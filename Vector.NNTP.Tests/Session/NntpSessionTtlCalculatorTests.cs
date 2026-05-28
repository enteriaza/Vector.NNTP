// <copyright file="NntpSessionTtlCalculatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Redis lease TTL sizing from idle timeout.

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Tests for <see cref="NntpSessionTtlCalculator"/>.
    /// </summary>
    [TestFixture]
    public sealed class NntpSessionTtlCalculatorTests
    {
        /// <summary>
        /// Verifies TTL is at least the minimum of 300 seconds.
        /// </summary>
        [Test]
        public void ComputeTtlSeconds_ShortIdleTimeout_UsesMinimum()
        {
            Assert.That(NntpSessionTtlCalculator.ComputeTtlSeconds(TimeSpan.FromSeconds(30)), Is.EqualTo(300));
        }

        /// <summary>
        /// Verifies TTL scales as twice idle timeout when above minimum.
        /// </summary>
        [Test]
        public void ComputeTtlSeconds_LongIdleTimeout_ScalesDouble()
        {
            Assert.That(NntpSessionTtlCalculator.ComputeTtlSeconds(TimeSpan.FromMinutes(10)), Is.EqualTo(1200));
        }
    }
}
