// <copyright file="RabbitMqPoolFlowControlMonitorTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using Vector.NNTP.MessageBus;
using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Connections;
using Vector.NNTP.MessageBus.Health;

namespace Vector.NNTP.Tests.MessageBus.Connections;

/// <summary>
/// Unit tests for <see cref="RabbitMqPoolFlowControlMonitor"/> integration with <see cref="ConnectionPool"/>.
/// </summary>
[TestFixture]
public sealed class RabbitMqPoolFlowControlMonitorTests
{
    /// <summary>
    /// Verifies the monitor quarantines long-blocked connections during a scan cycle.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ExecuteAsync_QuarantinesLongBlockedConnection()
    {
        RabbitMQOptions options = new()
        {
            Hosts = ["localhost"],
            ConnectionBlockedTimeout = TimeSpan.FromSeconds(1),
        };

        ConnectionPool pool = CreatePool(options);
        PooledConnection pooled = CreatePooledConnection();
        pooled.SetBlocked(true, DateTimeOffset.UtcNow.AddMinutes(-5));
        pool.SeedSnapshotForTesting(pooled);

        RabbitMqPoolHealth health = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        RabbitMqPoolFlowControlMonitor monitor = new(
            pool,
            health,
            Options.Create(options),
            NullLogger<RabbitMqPoolFlowControlMonitor>.Instance);

        await monitor.StartAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await Task.Delay(1500, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await monitor.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.That(pooled.IsStalled, Is.True);
    }

    /// <summary>Creates a <see cref="ConnectionPool"/> for tests without opening TCP connections.</summary>
    /// <param name="options">Options snapshot.</param>
    /// <returns>Configured pool instance.</returns>
    private static ConnectionPool CreatePool(RabbitMQOptions options)
    {
        Mock<IHostApplicationLifetime> lifetime = new();
        RabbitMqConnectionFactory factory = new(
            NullLogger<RabbitMqConnectionFactory>.Instance,
            lifetime.Object);
        return new ConnectionPool(
            factory,
            Options.Create(options),
            NullLogger<ConnectionPool>.Instance);
    }

    /// <summary>Creates a pooled connection backed by a mocked <see cref="IConnection"/>.</summary>
    /// <returns>Pooled connection entry.</returns>
    private static PooledConnection CreatePooledConnection()
    {
        Mock<IConnection> connection = new();
        return new PooledConnection(Guid.NewGuid(), 0, connection.Object);
    }
}
