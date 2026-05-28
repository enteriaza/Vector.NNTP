// <copyright file="NntpRateLimitConverterTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: decimal SI Mbps to bytes/sec conversion.

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Tests for <see cref="NntpRateLimitConverter"/>.
    /// </summary>
    [TestFixture]
    public sealed class NntpRateLimitConverterTests
    {
        /// <summary>
        /// Verifies 10 decimal SI Mbps converts to 1_250_000 bytes per second.
        /// </summary>
        [Test]
        public void MegabitsPerSecondToBytesPerSecond_10Mbps_Returns1250000()
        {
            Assert.That(NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(10), Is.EqualTo(1_250_000L));
        }

        /// <summary>
        /// Verifies 100 decimal SI Mbps converts to 12_500_000 bytes per second.
        /// </summary>
        [Test]
        public void MegabitsPerSecondToBytesPerSecond_100Mbps_Returns12500000()
        {
            Assert.That(NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(100), Is.EqualTo(12_500_000L));
        }

        /// <summary>
        /// Verifies non-positive input yields zero (unlimited/disabled).
        /// </summary>
        [Test]
        public void MegabitsPerSecondToBytesPerSecond_NonPositive_ReturnsZero()
        {
            Assert.That(NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(0), Is.EqualTo(0L));
            Assert.That(NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(-1), Is.EqualTo(0L));
        }
    }
}
