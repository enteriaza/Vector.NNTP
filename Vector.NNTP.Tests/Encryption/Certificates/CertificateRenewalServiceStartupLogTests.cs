// <copyright file="CertificateRenewalServiceStartupLogTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Vector.NNTP.Encryption.Certificates;
using Vector.NNTP.Encryption.Configuration;
using Vector.NNTP.Encryption.Dns;
using Vector.NNTP.Encryption.Telemetry;

namespace Vector.NNTP.Tests.Encryption.Certificates
{
    /// <summary>
    /// Verifies startup configuration summary logging for certificate renewal.
    /// </summary>
    [TestFixture]
    internal sealed class CertificateRenewalServiceStartupLogTests
    {
        /// <summary>
        /// Ensures enabled Let's Encrypt configuration emits the deployment summary without secrets.
        /// </summary>
        /// <returns>A task that completes when the hosted service start attempt finishes.</returns>
        [Test]
        public async Task ExecuteAsync_WhenEnabled_LogsEncryptionInitializedSummary()
        {
            List<string> logMessages = [];
            ILogger<CertificateRenewalService> logger = new ListLogger<CertificateRenewalService>(logMessages);

            var lifetime = new Mock<IHostApplicationLifetime>();
            lifetime.Setup(l => l.ApplicationStopping).Returns(CancellationToken.None);

            var environment = new Mock<IHostEnvironment>();
            environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);

            var dns = new NoOpDnsTxtPropagationProbe();
            ServiceProvider serviceProvider = new ServiceCollection().AddSingleton<IDnsTxtPropagationProbe>(dns).BuildServiceProvider();

            using CertificateRenewalService renewal = new(
                logger,
                Options.Create(new LetsEncryptOptions
                {
                    Enabled = true,
                    CertDir = Path.Combine(Path.GetTempPath(), "nntp-certs-test"),
                    AcmeAccountEmail = "ops@example.com",
                    DomainNames = ["example.com"],
                    CloudflareApiToken = "secret-token",
                    CloudflareZoneId = "zone-id",
                    ClusterBroadcastExchange = "certificates",
                    ClusterEnabled = true,
                }),
                Options.Create(new NntpServerOptions { NodeName = "CTN-01" }),
                lifetime.Object,
                environment.Object,
                dns,
                serviceProvider,
                new EncryptionMetrics());

            using CancellationTokenSource cts = new();
            Task startTask = renewal.StartAsync(cts.Token);
            for (int i = 0; i < 50; i++)
            {
                if (logMessages.Any(m => m.Contains("Encryption initialized", StringComparison.Ordinal)))
                {
                    break;
                }

                await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
            }

            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await startTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(
                logMessages.Any(m => m.Contains("Encryption initialized", StringComparison.Ordinal)
                    && m.Contains("example.com", StringComparison.Ordinal)
                    && m.Contains("Production", StringComparison.Ordinal)
                    && m.Contains("CTN-01", StringComparison.Ordinal)
                    && m.Contains("nntp-certs-test", StringComparison.Ordinal)),
                Is.True);
            Assert.That(logMessages.Any(m => m.Contains("secret-token", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Captures formatted log messages for assertions.
        /// </summary>
        /// <typeparam name="T">Logger category type.</typeparam>
        private sealed class ListLogger<T> : ILogger<T>
        {
            /// <summary>
            /// Backing message list.
            /// </summary>
            private readonly List<string> _messages;

            /// <summary>
            /// Initializes a new instance of the <see cref="ListLogger{T}"/> class.
            /// </summary>
            /// <param name="messages">Captured messages.</param>
            internal ListLogger(List<string> messages)
            {
                _messages = messages;
            }

            /// <summary>
            /// Returns null; scopes are not used by this test logger.
            /// </summary>
            /// <typeparam name="TState">Scope state type.</typeparam>
            /// <param name="state">Scope state.</param>
            /// <returns>Always null.</returns>
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            /// <summary>
            /// Returns true for all log levels so startup messages are captured.
            /// </summary>
            /// <param name="logLevel">Requested log level.</param>
            /// <returns>Always true.</returns>
            public bool IsEnabled(LogLevel logLevel) => true;

            /// <summary>
            /// Captures the formatted log message.
            /// </summary>
            /// <typeparam name="TState">Log state type.</typeparam>
            /// <param name="logLevel">Log level.</param>
            /// <param name="eventId">Event identifier.</param>
            /// <param name="state">Structured state.</param>
            /// <param name="exception">Optional exception.</param>
            /// <param name="formatter">Message formatter.</param>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }
}
