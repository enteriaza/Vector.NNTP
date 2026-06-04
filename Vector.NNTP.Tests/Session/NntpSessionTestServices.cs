// <copyright file="NntpSessionTestServices.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: wires in-memory session stack for protocol harness tests.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Vector.NNTP.Tests.Session
{
    /// <summary>
    /// Builds an in-memory session coordination stack for unit and protocol tests.
    /// </summary>
    internal static class NntpSessionTestServices
    {
        /// <summary>
        /// Creates default in-memory session services with a five-second idle timeout.
        /// </summary>
        /// <returns>Session services bundle for harness construction.</returns>
        public static NntpSessionTestBundle CreateDefault()
        {
            InMemorySessionDatabase database = new InMemorySessionDatabase();
            InMemorySessionCoordinator coordinator = new InMemorySessionCoordinator();
            InMemoryBlockQuotaCoordinator blockQuota = new InMemoryBlockQuotaCoordinator();
            TestOptionsMonitor<NntpRateAllocationOptions> rateOptions = new TestOptionsMonitor<NntpRateAllocationOptions>(new NntpRateAllocationOptions());
            NodeLocalRateAllocationCoordinator rateAllocation = new NodeLocalRateAllocationCoordinator(
                database,
                rateOptions,
                NullLogger<NodeLocalRateAllocationCoordinator>.Instance);
            NntpQuotaEnforcer quotaEnforcer = new NntpQuotaEnforcer(
                blockQuota,
                rateAllocation,
                NullLogger<NntpQuotaEnforcer>.Instance);
            TestOptionsMonitor<NntpSessionIdleOptions> idleOptions = new TestOptionsMonitor<NntpSessionIdleOptions>(
                new NntpSessionIdleOptions { IdleTimeout = TimeSpan.FromSeconds(5) });

            InMemoryTransitPeerCoordinator transitPeerCoordinator = new InMemoryTransitPeerCoordinator();
            return new NntpSessionTestBundle(
                database,
                coordinator,
                blockQuota,
                rateAllocation,
                quotaEnforcer,
                idleOptions,
                transitPeerCoordinator);
        }

        /// <summary>
        /// Creates session services using a shared admission coordinator (for multi-connection protocol tests).
        /// </summary>
        /// <param name="coordinator">Shared coordinator instance.</param>
        /// <returns>Session services bundle for harness construction.</returns>
        public static NntpSessionTestBundle CreateWithCoordinator(InMemorySessionCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            InMemorySessionDatabase database = new InMemorySessionDatabase();
            InMemoryBlockQuotaCoordinator blockQuota = new InMemoryBlockQuotaCoordinator();
            TestOptionsMonitor<NntpRateAllocationOptions> rateOptions = new TestOptionsMonitor<NntpRateAllocationOptions>(new NntpRateAllocationOptions());
            NodeLocalRateAllocationCoordinator rateAllocation = new NodeLocalRateAllocationCoordinator(
                database,
                rateOptions,
                NullLogger<NodeLocalRateAllocationCoordinator>.Instance);
            NntpQuotaEnforcer quotaEnforcer = new NntpQuotaEnforcer(
                blockQuota,
                rateAllocation,
                NullLogger<NntpQuotaEnforcer>.Instance);
            TestOptionsMonitor<NntpSessionIdleOptions> idleOptions = new TestOptionsMonitor<NntpSessionIdleOptions>(
                new NntpSessionIdleOptions { IdleTimeout = TimeSpan.FromSeconds(5) });

            InMemoryTransitPeerCoordinator transitPeerCoordinator = new InMemoryTransitPeerCoordinator();
            return new NntpSessionTestBundle(
                database,
                coordinator,
                blockQuota,
                rateAllocation,
                quotaEnforcer,
                idleOptions,
                transitPeerCoordinator);
        }

        /// <summary>
        /// In-memory session service bundle for test harness wiring.
        /// </summary>
        /// <param name="Database">Node-local session registry.</param>
        /// <param name="Coordinator">Admission coordinator.</param>
        /// <param name="BlockQuota">Block quota coordinator.</param>
        /// <param name="RateAllocation">Rate allocation coordinator.</param>
        /// <param name="QuotaEnforcer">Post-command quota enforcer.</param>
        /// <param name="IdleOptions">Idle timeout monitor for lease TTL sizing.</param>
        /// <param name="TransitPeerCoordinator">Transit peer admission coordinator.</param>
        internal readonly record struct NntpSessionTestBundle(
            ISessionDatabase Database,
            INntpSessionCoordinator Coordinator,
            INntpBlockQuotaCoordinator BlockQuota,
            INntpRateAllocationCoordinator RateAllocation,
            NntpQuotaEnforcer QuotaEnforcer,
            IOptionsMonitor<NntpSessionIdleOptions> IdleOptions,
            INntpTransitPeerCoordinator TransitPeerCoordinator);

        /// <summary>
        /// Minimal <see cref="IOptionsMonitor{T}"/> for tests.
        /// </summary>
        /// <typeparam name="T">Options type.</typeparam>
        internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
            where T : class, new()
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="TestOptionsMonitor{T}"/> class.
            /// </summary>
            /// <param name="value">Current options value.</param>
            public TestOptionsMonitor(T value)
            {
                this.CurrentValue = value;
            }

            /// <inheritdoc />
            public T CurrentValue { get; set; }

            /// <inheritdoc />
            public T Get(string? name) => this.CurrentValue;

            /// <inheritdoc />
            public IDisposable? OnChange(Action<T, string?> listener) => null;
        }
    }
}
