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
            Assert.That(NntpSessionTtlCalculator.ComputeTtlSeconds(30), Is.EqualTo(300));
        }

        /// <summary>
        /// Verifies TTL scales as twice idle timeout when above minimum.
        /// </summary>
        [Test]
        public void ComputeTtlSeconds_LongIdleTimeout_ScalesDouble()
        {
            Assert.That(NntpSessionTtlCalculator.ComputeTtlSeconds(600), Is.EqualTo(1200));
        }

        /// <summary>
        /// Verifies transit peer ZSET stale cutoff uses heartbeat scale, not idle timeout.
        /// </summary>
        [Test]
        public void ComputeTransitPeerLeaseSeconds_UsesHeartbeatScale()
        {
            Assert.That(
                NntpSessionTtlCalculator.ComputeTransitPeerLeaseSeconds(heartbeatIntervalSeconds: 60, ttlMinimumSeconds: 300),
                Is.EqualTo(300));
            Assert.That(
                NntpSessionTtlCalculator.ComputeTransitPeerLeaseSeconds(heartbeatIntervalSeconds: 120, ttlMinimumSeconds: 300),
                Is.EqualTo(360));
        }
    }
}
