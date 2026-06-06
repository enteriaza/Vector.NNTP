// <copyright file="AddMessageBusRegistrationTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Consuming;
using Vector.NNTP.MessageBus.DependencyInjection;
using Vector.NNTP.MessageBus.Metrics;
using Vector.NNTP.MessageBus.Publishing;

namespace Vector.NNTP.Tests.MessageBus.DependencyInjection
{
    /// <summary>
    /// Verifies MessageBus dependency injection registers the public MessageBus contracts.
    /// </summary>
    [TestFixture]
    internal sealed class AddMessageBusRegistrationTests
    {
        /// <summary>
        /// Ensures validator, connection factory, publisher pool, consumer manager, and metrics are registered.
        /// </summary>
        /// <returns>A task that completes when service resolution and disposal finish.</returns>
        [Test]
        public async Task AddMessageBus_RegistersValidatorConnectionFactoryAndPublisher()
        {
            ServiceCollection services = new();
            _ = services.AddLogging();
            Mock<IHostApplicationLifetime> lifetime = new();
            lifetime.Setup(l => l.ApplicationStopping).Returns(CancellationToken.None);
            _ = services.AddSingleton(lifetime.Object);
            _ = services.AddMessageBus(new RabbitMQOptions
            {
                Hosts = ["broker1"],
                MinConnections = 1,
                MaxConnections = 4,
            });

            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                Assert.That(provider.GetService<IValidateOptions<RabbitMQOptions>>(), Is.Not.Null);
                Assert.That(provider.GetService<IRabbitMqConnectionFactory>(), Is.Not.Null);
                Assert.That(provider.GetService<IRabbitMqPublisherPool>(), Is.Not.Null);
                Assert.That(provider.GetService<IRabbitMqConsumerManager>(), Is.Not.Null);
                Assert.That(provider.GetService<MessageBusMetrics>(), Is.Not.Null);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
