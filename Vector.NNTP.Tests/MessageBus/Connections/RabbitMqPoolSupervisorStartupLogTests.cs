// <copyright file="RabbitMqPoolSupervisorStartupLogTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Health;
using Vector.NNTP.MessageBus.Metrics;

namespace Vector.NNTP.Tests.MessageBus.Connections
{
    /// <summary>
    /// Verifies startup configuration summary logging for the MessageBus pool supervisor.
    /// </summary>
    [TestFixture]
    internal sealed class RabbitMqPoolSupervisorStartupLogTests
    {
        /// <summary>
        /// Ensures pool startup emits the deployment summary without credentials or virtual-host secrets.
        /// </summary>
        /// <returns>A task that completes when supervisor startup finishes.</returns>
        [Test]
        public async Task StartAsync_WhenPoolStarts_LogsMessageBusInitializedSummary()
        {
            List<string> logMessages = [];
            ILogger<RabbitMqPoolSupervisor> logger = new ListLogger<RabbitMqPoolSupervisor>(logMessages);

            Mock<IConnection> connection = new();
            Mock<IRabbitMqConnectionFactory> factory = new();
            factory
                .Setup(f => f.CreateConnectionAsync(It.IsAny<RabbitMQOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection.Object);

            RabbitMQOptions options = new()
            {
                Hosts = ["broker1", "broker2", "broker3"],
                MinConnections = 4,
                MaxConnections = 32,
                EnableSsl = true,
                Username = "secret-user",
                Password = "secret-password",
                VirtualHost = "secret-vhost",
            };

            ConnectionPool pool = new(
                factory.Object,
                Options.Create(options),
                NullLogger<ConnectionPool>.Instance);
            RabbitMqPoolHealth health = new(new MessageBusMetrics());
            RabbitMqPoolSupervisor supervisor = new(pool, health, Options.Create(options), logger);

            await supervisor.StartAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                logMessages.Any(m => m.Contains("MessageBus initialized", StringComparison.Ordinal)
                    && m.Contains("BrokerCount=3", StringComparison.Ordinal)
                    && m.Contains("MinConnections=4", StringComparison.Ordinal)
                    && m.Contains("MaxConnections=32", StringComparison.Ordinal)
                    && m.Contains("Tls=True", StringComparison.Ordinal)
                    && m.Contains("PublisherConfirms=True", StringComparison.Ordinal)),
                Is.True);
            Assert.That(logMessages.Any(m => m.Contains("secret-user", StringComparison.Ordinal)), Is.False);
            Assert.That(logMessages.Any(m => m.Contains("secret-password", StringComparison.Ordinal)), Is.False);
            Assert.That(logMessages.Any(m => m.Contains("secret-vhost", StringComparison.Ordinal)), Is.False);

            await pool.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Captures formatted log messages for assertions.
        /// </summary>
        /// <typeparam name="T">Logger category type.</typeparam>
        private sealed class ListLogger<T> : ILogger<T>
        {
            /// <summary>Backing message list.</summary>
            private readonly List<string> _messages;

            /// <summary>
            /// Initializes a new instance of the <see cref="ListLogger{T}"/> class.
            /// </summary>
            /// <param name="messages">Captured messages.</param>
            internal ListLogger(List<string> messages)
            {
                _messages = messages;
            }

            /// <summary>Returns null; scopes are not used by this test logger.</summary>
            /// <typeparam name="TState">Scope state type.</typeparam>
            /// <param name="state">Scope state.</param>
            /// <returns>Always null.</returns>
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            /// <summary>Returns true so source-generated log methods emit messages.</summary>
            /// <param name="logLevel">Requested log level.</param>
            /// <returns>Always true.</returns>
            public bool IsEnabled(LogLevel logLevel) => true;

            /// <summary>Appends the formatted message to the capture list.</summary>
            /// <typeparam name="TState">Log state type.</typeparam>
            /// <param name="logLevel">Emitted log level.</param>
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
