// <copyright file="EncryptionTlsCertificateSourceTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vector.NNTP.Encryption.Certificates;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Dns;
using Vector.NNTP.Encryption.Telemetry;
using Vector.NNTP.Sockets.Tls;
using Vector.NNTP.Tests.Encryption;

namespace Vector.NNTP.Tests.Sockets
{
    /// <summary>
    /// Unit tests for <see cref="EncryptionTlsCertificateSource"/>.
    /// </summary>
    [TestFixture]
    internal sealed class EncryptionTlsCertificateSourceTests
    {
        /// <summary>
        /// <see cref="EncryptionTlsCertificateSource"/> returns the same certificate as <see cref="ICertificateRenewalPublisher.GetCurrentCertificate"/>.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task GetServerCertificateAsync_ReturnsRenewalServiceCurrent()
        {
            using X509Certificate2 expected = CreateSelfSigned();
            using var renewal = CreateRenewalService();
            renewal.ActivateCertificate(expected);

            var source = new EncryptionTlsCertificateSource(renewal);
            X509Certificate2? actual = await source.GetServerCertificateAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
        }

        /// <summary>
        /// <see cref="EncryptionTlsCertificateSource"/> returns null when renewal has no certificate yet.
        /// </summary>
        /// <returns>A task that completes when assertions finish.</returns>
        [Test]
        public async Task GetServerCertificateAsync_WhenNoCert_ReturnsNull()
        {
            using var renewal = CreateRenewalService();
            var source = new EncryptionTlsCertificateSource(renewal);

            X509Certificate2? actual = await source.GetServerCertificateAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(actual, Is.Null);
        }

        /// <summary>
        /// Creates a disabled-ACME renewal service for unit tests.
        /// </summary>
        /// <returns>Configured renewal service instance.</returns>
        private static CertificateRenewalService CreateRenewalService()
        {
            var lifetime = new Mock<IHostApplicationLifetime>();
            lifetime.Setup(l => l.ApplicationStopping).Returns(CancellationToken.None);

            var environment = new Mock<IHostEnvironment>();
            environment.Setup(e => e.EnvironmentName).Returns(Environments.Development);

            var dns = new NoOpDnsTxtPropagationProbe();
            ServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton<IDnsTxtPropagationProbe>(dns)
                .BuildServiceProvider();

            return new CertificateRenewalService(
                NullLogger<CertificateRenewalService>.Instance,
                Options.Create(new LetsEncryptOptions { Enabled = false }),
                Options.Create(new NntpServerOptions { NodeName = "test-node" }),
                lifetime.Object,
                environment.Object,
                dns,
                serviceProvider,
                new EncryptionMetrics());
        }

        /// <summary>
        /// Creates a short-lived self-signed certificate for TLS tests.
        /// </summary>
        /// <returns>Disposable test certificate.</returns>
        private static X509Certificate2 CreateSelfSigned()
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=unit-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        }
    }
}
