// <copyright file="AddEncryptionRegistrationTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Vector.NNTP.Encryption.Certificates;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.DependencyInjection;
using Vector.NNTP.Encryption.Telemetry;

namespace Vector.NNTP.Tests.Encryption.DependencyInjection
{
    /// <summary>
    /// Verifies <see cref="Vector.NNTP.Encryption.DependencyInjection.ServiceCollectionExtensions.AddEncryption(IServiceCollection, LetsEncryptOptions, NntpServerOptions)"/> registers the public bridge and validator.
    /// </summary>
    [TestFixture]
    internal sealed class AddEncryptionRegistrationTests
    {
        /// <summary>
        /// Ensures encryption DI registers the renewal publisher bridge and options validator.
        /// </summary>
        [Test]
        public void AddEncryption_RegistersPublisherValidatorAndMetrics()
        {
            ServiceCollection services = new();
            _ = services.AddLogging();
            _ = services.AddSingleton(Mock.Of<IHostApplicationLifetime>());
            _ = services.AddSingleton(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development));
            _ = services.AddEncryption(
                new LetsEncryptOptions { Enabled = false, CertDir = "certs", AcmeAccountEmail = "ops@example.com" },
                new NntpServerOptions { NodeName = "CTN-01" });

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetService<ICertificateRenewalPublisher>(), Is.Not.Null);
            Assert.That(provider.GetService<IValidateOptions<LetsEncryptOptions>>(), Is.InstanceOf<LetsEncryptOptionsValidator>());
            Assert.That(provider.GetService<EncryptionMetrics>(), Is.Not.Null);
            Assert.That(provider.GetService<CertificateRenewalService>(), Is.Not.Null);
        }
    }
}
