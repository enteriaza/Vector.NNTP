// <copyright file="NntpSessionCoordinationOptionsBindingTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// COLD PATH: Redis coordination options bind from configuration.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vector.NNTP.Session.Redis.Configuration;
using Vector.NNTP.Session.Redis.Connections;
using Vector.NNTP.Session.Redis.Coordination;
using Vector.NNTP.Session.Redis.DependencyInjection;

namespace Vector.NNTP.Tests.Session.Redis
{
    /// <summary>
    /// Tests for <see cref="NntpSessionCoordinationOptions"/> configuration binding.
    /// </summary>
    [TestFixture]
    public sealed class NntpSessionCoordinationOptionsBindingTests
    {
        /// <summary>
        /// Verifies the Redis section binds coordination options from <c>Redis:Hosts</c>.
        /// </summary>
        /// <returns>A task representing the asynchronous test operation.</returns>
        [Test]
        public async Task AddNntpSessionRedis_BindsRedisSection()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Redis:Hosts:0"] = "127.0.0.1",
                    ["Redis:Hosts:1"] = "127.0.0.2",
                    ["Redis:Port"] = "6379",
                    ["Redis:Retry"] = "3",
                    ["Redis:TimeoutSeconds"] = "3",
                    ["Redis:KeyPrefix"] = "test:",
                    ["Redis:HeartbeatIntervalSeconds"] = "30",
                    ["Redis:ReconciliationIntervalSeconds"] = "0",
                })
                .Build();

            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
            services.AddNntpSessionRedis(configuration);

            ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            try
            {
                NntpSessionCoordinationOptions options = provider.GetRequiredService<IOptions<NntpSessionCoordinationOptions>>().Value;

                Assert.That(options.Hosts, Has.Length.EqualTo(2));
                Assert.That(options.Hosts[0], Is.EqualTo("127.0.0.1"));
                Assert.That(options.Port, Is.EqualTo(6379));
                Assert.That(options.Retry, Is.EqualTo(3));
                Assert.That(options.TimeoutSeconds, Is.EqualTo(3));
                Assert.That(options.KeyPrefix, Is.EqualTo("test:"));
                Assert.That(options.HeartbeatIntervalSeconds, Is.EqualTo(30));
                Assert.That(provider.GetRequiredService<IRedisConnectionAccessor>(), Is.TypeOf<RedisConnectionAccessor>());
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies empty <c>Redis:Hosts</c> fails cross-property validation.
        /// </summary>
        [Test]
        public void Validator_RejectsEmptyHosts()
        {
            NntpSessionCoordinationOptions options = new() { Hosts = [] };
            NntpSessionCoordinationOptionsValidator validator = new(NullLogger<NntpSessionCoordinationOptionsValidator>.Instance);
            ValidateOptionsResult result = validator.Validate(null, options);
            Assert.That(result.Failed, Is.True);
        }

        /// <summary>
        /// Verifies <see cref="RedisMultiplexerFactory"/> maps options to StackExchange.Redis configuration.
        /// </summary>
        [Test]
        public void BuildConfigurationOptions_MapsHostsPortRetryAndTimeout()
        {
            NntpSessionCoordinationOptions options = new()
            {
                Hosts = ["127.0.0.1", "127.0.0.2"],
                Port = 6380,
                Retry = 5,
                TimeoutSeconds = 10,
            };

            StackExchange.Redis.ConfigurationOptions configuration =
                RedisMultiplexerFactory.BuildConfigurationOptions(options);

            Assert.That(configuration.ConnectRetry, Is.EqualTo(5));
            Assert.That(configuration.ConnectTimeout, Is.EqualTo(10_000));
            Assert.That(configuration.SyncTimeout, Is.EqualTo(10_000));
            Assert.That(configuration.EndPoints, Has.Count.EqualTo(2));
        }

        /// <summary>Test host environment stub.</summary>
        private sealed class TestHostEnvironment : IHostEnvironment
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