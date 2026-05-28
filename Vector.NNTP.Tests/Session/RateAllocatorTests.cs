// <copyright file="RateAllocatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Tests for <see cref="RateAllocator"/> fair-share math.
    /// </summary>
    [TestFixture]
    public sealed class RateAllocatorTests
    {
        /// <summary>
        /// Verifies decimal SI Mbps conversion examples from documentation.
        /// </summary>
        [Test]
        public void MegabitsPerSecondToBytesPerSecond_UsesDecimalSi()
        {
            Assert.That(NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(10), Is.EqualTo(1_250_000L));
            Assert.That(NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(100), Is.EqualTo(12_500_000L));
        }

        /// <summary>
        /// Verifies fair-share division never multiplies account rate by session count.
        /// </summary>
        [Test]
        public void ComputePerSessionSendRate_DividesAccountCeiling()
        {
            long accountRate = NntpRateLimitConverter.MegabitsPerSecondToBytesPerSecond(100);
            long perSession = RateAllocator.ComputePerSessionSendRateBytesPerSecond(accountRate, 4);
            Assert.That(perSession, Is.EqualTo(accountRate / 4));
        }
    }
}
