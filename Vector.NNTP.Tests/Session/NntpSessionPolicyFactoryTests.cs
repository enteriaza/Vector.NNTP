// <copyright file="NntpSessionPolicyFactoryTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Tests for <see cref="NntpSessionPolicyFactory"/> R/B mapping.
    /// </summary>
    [TestFixture]
    public sealed class NntpSessionPolicyFactoryTests
    {
        private readonly Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();

        /// <summary>
        /// Rate-limited accounts use Mbps-derived bytes per second.
        /// </summary>
        [Test]
        public void Create_RateLimited_MapsMbpsToBytesPerSecond()
        {
            NntpAccountLimits limits = new("alice", 'R', 10, 0, 2, 1, "cust");
            NntpSessionPolicy policy = NntpSessionPolicyFactory.Create(limits, allowPosting: true, this.normalizer);
            Assert.That(policy.AccountType, Is.EqualTo(NntpAccountType.RateLimited));
            Assert.That(policy.RateBytesPerSecond, Is.EqualTo(1_250_000L));
            Assert.That(policy.ByteLimit, Is.EqualTo(0));
        }

        /// <summary>
        /// Byte-limited accounts carry byte quota only.
        /// </summary>
        [Test]
        public void Create_ByteLimited_MapsByteLimit()
        {
            NntpAccountLimits limits = new("bob", 'B', 0, 5_000_000L, 1, 1, "cust");
            NntpSessionPolicy policy = NntpSessionPolicyFactory.Create(limits, allowPosting: false, this.normalizer);
            Assert.That(policy.AccountType, Is.EqualTo(NntpAccountType.ByteLimited));
            Assert.That(policy.ByteLimit, Is.EqualTo(5_000_000L));
            Assert.That(policy.RateBytesPerSecond, Is.EqualTo(0));
        }
    }
}
