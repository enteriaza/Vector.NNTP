// <copyright file="InMemorySessionCoordinatorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: in-memory admission acquire/release counts.

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Tests for <see cref="InMemorySessionCoordinator"/>.
    /// </summary>
    [TestFixture]
    public sealed class InMemorySessionCoordinatorTests
    {
        private readonly Blake3AccountKeyNormalizer _normalizer = new Blake3AccountKeyNormalizer();

        /// <summary>
        /// Verifies session limit enforcement denies additional sessions.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task TryAdmitAsync_ExceedsSessionLimit_ReturnsMaxSessionsExceeded()
        {
            InMemorySessionCoordinator coordinator = new InMemorySessionCoordinator();
            NntpSessionPolicy policy = NntpSessionPolicyFactory.Create(
                new NntpAccountLimits("user", 'R', 0, 0, 1, 0, string.Empty),
                allowPosting: true,
                this._normalizer);

            NntpSessionAdmissionResult first = await coordinator.TryAdmitAsync(policy, "s1", "127.0.0.1", 60, CancellationToken.None).ConfigureAwait(false);
            NntpSessionAdmissionResult second = await coordinator.TryAdmitAsync(policy, "s2", "127.0.0.1", 60, CancellationToken.None).ConfigureAwait(false);

            Assert.That(first, Is.EqualTo(NntpSessionAdmissionResult.Success));
            Assert.That(second, Is.EqualTo(NntpSessionAdmissionResult.MaxSessionsExceeded));
        }

        /// <summary>
        /// Verifies release allows a subsequent admission.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task ReleaseAsync_AfterAdmit_AllowsReAdmission()
        {
            InMemorySessionCoordinator coordinator = new InMemorySessionCoordinator();
            NntpSessionPolicy policy = NntpSessionPolicyFactory.Create(
                new NntpAccountLimits("user", 'R', 0, 0, 1, 0, string.Empty),
                allowPosting: true,
                this._normalizer);

            _ = await coordinator.TryAdmitAsync(policy, "s1", "127.0.0.1", 60, CancellationToken.None).ConfigureAwait(false);
            await coordinator.ReleaseAsync(policy, "s1", "127.0.0.1", CancellationToken.None).ConfigureAwait(false);
            NntpSessionAdmissionResult second = await coordinator.TryAdmitAsync(policy, "s2", "127.0.0.1", 60, CancellationToken.None).ConfigureAwait(false);

            Assert.That(second, Is.EqualTo(NntpSessionAdmissionResult.Success));
        }
    }
}
