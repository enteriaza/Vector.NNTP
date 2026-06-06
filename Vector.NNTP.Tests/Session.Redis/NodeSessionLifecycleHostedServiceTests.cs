// <copyright file="NodeSessionLifecycleHostedServiceTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vector.NNTP.Session.Redis.Connections;
using Vector.NNTP.Session.Redis.DependencyInjection;
using Vector.NNTP.Session.Redis.HostedServices;

namespace Vector.NNTP.Tests.Session.Redis
{
    /// <summary>
    /// Unit tests for <see cref="NodeSessionLifecycleHostedService"/> startup and shutdown purge behavior.
    /// </summary>
    [TestFixture]
    public sealed class NodeSessionLifecycleHostedServiceTests
    {
        /// <summary>
        /// Verifies startup invokes node purge before other session hosted services are registered.
        /// </summary>
        [Test]
        public void AddNntpSessionRedis_RegistersLifecycleBeforeHeartbeat()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Redis:Hosts:0"] = "127.0.0.1",
                    ["Redis:Port"] = "6379",
                    ["Redis:ReconciliationIntervalSeconds"] = "0",
                    ["NntpServer:NodeName"] = "test-node",
                })
                .Build();
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostEnvironment>(new LifecycleTestHostEnvironment());
            _ = services.AddNntpSessionRedis(configuration);
            List<ServiceDescriptor> hosted = services
                .Where(static d => d.ServiceType == typeof(IHostedService))
                .ToList();
            int lifecycleIndex = hosted.FindIndex(static d => d.ImplementationType == typeof(NodeSessionLifecycleHostedService));
            int heartbeatIndex = hosted.FindIndex(static d => d.ImplementationType == typeof(RedisSessionHeartbeatHostedService));
            int supervisorIndex = hosted.FindIndex(static d => d.ImplementationType == typeof(RedisMultiplexerPoolSupervisor));
            Assert.That(lifecycleIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(heartbeatIndex, Is.GreaterThan(lifecycleIndex));
            Assert.That(supervisorIndex, Is.LessThan(lifecycleIndex));
        }

        /// <summary>
        /// Verifies <see cref="NodeSessionLifecycleHostedService.StartAsync"/> purges the configured node.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task StartAsync_PurgesConfiguredNode()
        {
            var registry = new Mock<INodeSessionRegistry>();
            _ = registry
                .Setup(static r => r.PurgeNodeAsync("nntpd01", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NodeSessionPurgeResult(1, 2, 12.5, false, 0));
            var sessionDatabase = new InMemorySessionDatabase();
            var sessionCoordinator = new Mock<INntpSessionCoordinator>();
            var transitCoordinator = new Mock<INntpTransitPeerCoordinator>();
            var nodeOptions = Options.Create(new NntpNodeIdentityOptions { NodeName = "nntpd01" });
            var service = new NodeSessionLifecycleHostedService(
                registry.Object,
                sessionDatabase,
                sessionCoordinator.Object,
                transitCoordinator.Object,
                nodeOptions,
                NullLogger<NodeSessionLifecycleHostedService>.Instance);
            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
            registry.Verify(
                static r => r.PurgeNodeAsync("nntpd01", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies shutdown releases survivors then purges again.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task StopAsync_ReleasesSurvivorsThenPurges()
        {
            var registry = new Mock<INodeSessionRegistry>();
            _ = registry
                .Setup(static r => r.PurgeNodeAsync("nntpd01", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NodeSessionPurgeResult(0, 0, 0, false, 0));
            var sessionDatabase = new InMemorySessionDatabase();
            SessionContext survivor = new(
                "survivor",
                System.Net.IPAddress.Loopback,
                "[127.0.0.1:0]",
                DateTimeOffset.UtcNow,
                "v1",
                "nntpd01",
                "peer-1");
            _ = sessionDatabase.TryAdd(survivor);
            var transitCoordinator = new Mock<INntpTransitPeerCoordinator>();
            var service = new NodeSessionLifecycleHostedService(
                registry.Object,
                sessionDatabase,
                new Mock<INntpSessionCoordinator>().Object,
                transitCoordinator.Object,
                Options.Create(new NntpNodeIdentityOptions { NodeName = "nntpd01" }),
                NullLogger<NodeSessionLifecycleHostedService>.Instance);
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
            transitCoordinator.Verify(
                static c => c.ReleaseAsync("peer-1", "survivor", "nntpd01", It.IsAny<CancellationToken>()),
                Times.Once);
            registry.Verify(
                static r => r.PurgeNodeAsync("nntpd01", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Minimal host environment for DI registration tests.
        /// </summary>
        private sealed class LifecycleTestHostEnvironment : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = Environments.Development;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Vector.NNTP.Tests";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
