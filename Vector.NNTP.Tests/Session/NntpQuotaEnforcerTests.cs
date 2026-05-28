// <copyright file="NntpQuotaEnforcerTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: block quota and rate refresh behavior for NntpQuotaEnforcer.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Unit tests for <see cref="NntpQuotaEnforcer"/>.
    /// </summary>
    [TestFixture]
    public sealed class NntpQuotaEnforcerTests
    {
        private static readonly Blake3AccountKeyNormalizer Normalizer = new Blake3AccountKeyNormalizer();

        /// <summary>
        /// Verifies byte-limited accounts with zero byte limit skip block quota enforcement.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ApplyBlockQuota_SkipsWhenByteLimitZero()
        {
            CountingBlockQuotaCoordinator coordinator = new CountingBlockQuotaCoordinator();
            NntpQuotaEnforcer enforcer = CreateEnforcer(coordinator);
            NntpSessionPolicy policy = CreatePolicy(NntpAccountType.ByteLimited, byteLimit: 0);

            QuotaEnforcementResult result = await enforcer.ApplyBlockQuotaAfterCommandAsync(
                policy,
                "session-1",
                commandBytes: 128,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ShouldDeauthorize, Is.False);
            Assert.That(coordinator.InitializeCalls, Is.EqualTo(0));
            Assert.That(coordinator.DecrementCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies finite byte-limited accounts decrement quota after each command.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ApplyBlockQuota_DecrementsForByteLimitedAccount()
        {
            CountingBlockQuotaCoordinator coordinator = new CountingBlockQuotaCoordinator();
            NntpQuotaEnforcer enforcer = CreateEnforcer(coordinator);
            NntpSessionPolicy policy = CreatePolicy(NntpAccountType.ByteLimited, byteLimit: 10_000);

            QuotaEnforcementResult result = await enforcer.ApplyBlockQuotaAfterCommandAsync(
                policy,
                "session-1",
                commandBytes: 128,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ShouldDeauthorize, Is.False);
            Assert.That(coordinator.InitializeCalls, Is.EqualTo(1));
            Assert.That(coordinator.DecrementCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies rate-limited accounts skip block quota enforcement.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ApplyBlockQuota_SkipsForRateLimitedAccount()
        {
            CountingBlockQuotaCoordinator coordinator = new CountingBlockQuotaCoordinator();
            NntpQuotaEnforcer enforcer = CreateEnforcer(coordinator);
            NntpSessionPolicy policy = CreatePolicy(NntpAccountType.RateLimited, rateMbps: 100);

            QuotaEnforcementResult result = await enforcer.ApplyBlockQuotaAfterCommandAsync(
                policy,
                "session-1",
                commandBytes: 128,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ShouldDeauthorize, Is.False);
            Assert.That(coordinator.InitializeCalls, Is.EqualTo(0));
            Assert.That(coordinator.DecrementCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies block quota exhaustion forces deauthentication.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task ApplyBlockQuota_ForcesDeauthWhenQuotaExhausted()
        {
            InMemoryBlockQuotaCoordinator coordinator = new InMemoryBlockQuotaCoordinator();
            NntpQuotaEnforcer enforcer = CreateEnforcer(new InMemoryBlockQuotaCoordinator());
            NntpSessionPolicy policy = CreatePolicy(NntpAccountType.ByteLimited, byteLimit: 50);

            _ = await enforcer.ApplyBlockQuotaAfterCommandAsync(policy, "session-1", 40, CancellationToken.None).ConfigureAwait(false);
            QuotaEnforcementResult result = await enforcer.ApplyBlockQuotaAfterCommandAsync(
                policy,
                "session-1",
                20,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ShouldDeauthorize, Is.True);
            Assert.That(result.Reason, Is.EqualTo("block_quota"));
        }

        /// <summary>
        /// Verifies refresh returns zero for non-rate-limited accounts.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
        public async Task RefreshRateLimit_ReturnsZeroForByteLimitedAccount()
        {
            InMemorySessionDatabase database = new InMemorySessionDatabase();
            NodeLocalRateAllocationCoordinator rateAllocation = new NodeLocalRateAllocationCoordinator(
                database,
                new NntpSessionTestServices.TestOptionsMonitor<NntpRateAllocationOptions>(new NntpRateAllocationOptions()),
                NullLogger<NodeLocalRateAllocationCoordinator>.Instance);
            NntpQuotaEnforcer enforcer = new NntpQuotaEnforcer(
                new InMemoryBlockQuotaCoordinator(),
                rateAllocation,
                NullLogger<NntpQuotaEnforcer>.Instance);
            NntpSessionPolicy policy = CreatePolicy(NntpAccountType.ByteLimited, byteLimit: 10_000);

            long perSession = await enforcer.RefreshRateLimitAsync(policy, CancellationToken.None).ConfigureAwait(false);

            Assert.That(perSession, Is.EqualTo(0));
        }

        /// <summary>
        /// Builds an enforcer with the supplied block quota coordinator.
        /// </summary>
        /// <param name="blockQuota">Block quota coordinator.</param>
        /// <returns>Configured enforcer.</returns>
        private static NntpQuotaEnforcer CreateEnforcer(INntpBlockQuotaCoordinator blockQuota)
        {
            InMemorySessionDatabase database = new InMemorySessionDatabase();
            NodeLocalRateAllocationCoordinator rateAllocation = new NodeLocalRateAllocationCoordinator(
                database,
                new NntpSessionTestServices.TestOptionsMonitor<NntpRateAllocationOptions>(new NntpRateAllocationOptions()),
                NullLogger<NodeLocalRateAllocationCoordinator>.Instance);
            return new NntpQuotaEnforcer(blockQuota, rateAllocation, NullLogger<NntpQuotaEnforcer>.Instance);
        }

        /// <summary>
        /// Creates a test policy for the given account type.
        /// </summary>
        /// <param name="accountType">Rate or byte limited.</param>
        /// <param name="rateMbps">Rate limit Mbps when rate-limited.</param>
        /// <param name="byteLimit">Byte quota when byte-limited.</param>
        /// <returns>Session policy.</returns>
        private static NntpSessionPolicy CreatePolicy(
            NntpAccountType accountType,
            int rateMbps = 0,
            long byteLimit = 0)
        {
            char typeChar = accountType == NntpAccountType.ByteLimited ? 'B' : 'R';
            NntpAccountLimits limits = new("alice", typeChar, rateMbps, byteLimit, 0, 0, string.Empty);
            return NntpSessionPolicyFactory.Create(limits, allowPosting: true, Normalizer);
        }

        /// <summary>
        /// Test double that counts block quota coordinator invocations.
        /// </summary>
        private sealed class CountingBlockQuotaCoordinator : INntpBlockQuotaCoordinator
        {
            /// <summary>
            /// Gets the number of initialize calls observed.
            /// </summary>
            public int InitializeCalls { get; private set; }

            /// <summary>
            /// Gets the number of decrement calls observed.
            /// </summary>
            public int DecrementCalls { get; private set; }

            /// <inheritdoc />
            public ValueTask<bool> TryInitializeQuotaAsync(string accountKey, long initialBytes, CancellationToken cancellationToken)
            {
                _ = accountKey;
                _ = initialBytes;
                _ = cancellationToken;
                this.InitializeCalls++;
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask<long> DecrementAsync(string accountKey, long bytes, CancellationToken cancellationToken)
            {
                _ = accountKey;
                _ = cancellationToken;
                this.DecrementCalls++;
                return ValueTask.FromResult(1000L - bytes);
            }
        }
    }
}
