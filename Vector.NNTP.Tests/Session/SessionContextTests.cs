// <copyright file="SessionContextTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: authentication state CAS transitions.

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Tests for <see cref="SessionContext"/> authentication CAS semantics.
    /// </summary>
    [TestFixture]
    public sealed class SessionContextTests
    {
        private const string TestConnectionPrefix = "[127.0.0.1:0]";

        /// <summary>
        /// Verifies authenticating transition succeeds from unauthenticated state.
        /// </summary>
        [Test]
        public void TryBeginAuthenticating_FromUnauthenticated_Succeeds()
        {
            SessionContext ctx = new SessionContext("sid", IPAddress.Loopback, TestConnectionPrefix, DateTimeOffset.UtcNow, "v1", "test-node");
            Assert.That(ctx.TryBeginAuthenticating(AuthenticatingPhase.SaslContinuation), Is.True);
            Assert.That(ctx.AuthenticationState, Is.EqualTo(AuthenticationState.Authenticating));
        }

        /// <summary>
        /// Verifies duplicate authenticating attempts fail CAS.
        /// </summary>
        [Test]
        public void TryBeginAuthenticating_WhenAlreadyAuthenticating_Fails()
        {
            SessionContext ctx = new SessionContext("sid", IPAddress.Loopback, TestConnectionPrefix, DateTimeOffset.UtcNow, "v1", "test-node");
            Assert.That(ctx.TryBeginAuthenticating(AuthenticatingPhase.SaslContinuation), Is.True);
            Assert.That(ctx.TryBeginAuthenticating(AuthenticatingPhase.SaslContinuation), Is.False);
        }

        /// <summary>
        /// Verifies authenticated transition from pending admission phase.
        /// </summary>
        [Test]
        public void NodeName_IsSetAtConstruction()
        {
            SessionContext ctx = new SessionContext("sid", IPAddress.Loopback, TestConnectionPrefix, DateTimeOffset.UtcNow, "v1", "nntpd01");
            Assert.That(ctx.NodeName, Is.EqualTo("nntpd01"));
        }

        /// <summary>
        /// Verifies authenticated transition from pending admission phase.
        /// </summary>
        [Test]
        public void TryCompleteAuthentication_FromPendingAdmission_Succeeds()
        {
            Blake3AccountKeyNormalizer normalizer = new Blake3AccountKeyNormalizer();
            NntpSessionPolicy policy = NntpSessionPolicyFactory.Create(
                new NntpAccountLimits("user", 'R', 0, 0, 0, 0, string.Empty),
                allowPosting: true,
                normalizer);
            SessionContext ctx = new SessionContext("sid", IPAddress.Loopback, TestConnectionPrefix, DateTimeOffset.UtcNow, "v1", "test-node");
            Assert.That(ctx.TryBeginAuthenticating(AuthenticatingPhase.SaslContinuation), Is.True);
            Assert.That(ctx.TryBindPendingAuthentication("user", policy.AccountKey, policy, AuthenticatingPhase.PendingAdmission), Is.True);
            Assert.That(ctx.TryCompleteAuthentication(), Is.True);
            Assert.That(ctx.AuthenticationState, Is.EqualTo(AuthenticationState.Authenticated));
        }
    }
}
