// <copyright file="ConnectionPoolTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using MessageBus;
using MessageBus.Configuration;
using MessageBus.Connections;
using MessageBus.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;

namespace MessageBus.Tests.Connections;

/// <summary>
/// Unit tests for <see cref="ConnectionPool"/> waiter backpressure and blocked-connection behavior.
/// </summary>
[TestFixture]
public sealed class ConnectionPoolTests
{
    /// <summary>
    /// Verifies <see cref="RabbitMQOptions.MaxPendingLeaseWaiters"/> rejects additional waiters.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task AcquirePublisherSlotAsync_WhenWaiterCapExceeded_ThrowsUnavailable()
    {
        RabbitMQOptions options = new()
        {
            Hosts = ["localhost"],
            ChannelPoolSize = 1,
            ChannelLeaseTimeout = TimeSpan.FromSeconds(5),
            MaxPendingLeaseWaiters = 1,
        };

        ConnectionPool pool = CreatePool(options);
        PooledConnection pooled = CreatePooledConnection();
        Assert.That(pooled.TryAcquireSlot(), Is.True);
        pool.SeedSnapshotForTesting(pooled);

        using CancellationTokenSource waiterCts = new();
        Task<PublisherSlotLease> blockedWait = pool.AcquirePublisherSlotAsync(waiterCts.Token);
        await Task.Delay(100).ConfigureAwait(false);

        MessageBusUnavailableException ex = Assert.ThrowsAsync<MessageBusUnavailableException>(
            () => pool.AcquirePublisherSlotAsync(CancellationToken.None))!;

        Assert.That(ex.Message, Does.Contain("waiter queue is full"));

        waiterCts.Cancel();
        try
        {
            await blockedWait.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Verifies blocked connections are skipped until lease timeout.
    /// </summary>
    [Test]
    public void AcquirePublisherSlotAsync_WhenOnlyBlockedConnection_ThrowsLeaseTimeout()
    {
        RabbitMQOptions options = new()
        {
            Hosts = ["localhost"],
            ChannelPoolSize = 4,
            ChannelLeaseTimeout = TimeSpan.FromMilliseconds(200),
            MaxPendingLeaseWaiters = 8,
        };

        ConnectionPool pool = CreatePool(options);
        PooledConnection pooled = CreatePooledConnection();
        pooled.SetBlocked(true);
        pool.SeedSnapshotForTesting(pooled);

        Assert.ThrowsAsync<MessageBusLeaseTimeoutException>(
            () => pool.AcquirePublisherSlotAsync(CancellationToken.None));
    }

    /// <summary>
    /// Verifies prolonged blocking quarantines the connection as stalled.
    /// </summary>
    [Test]
    public void EnforceBlockedQuarantine_WhenBlockedBeyondTimeout_MarksStalled()
    {
        ConnectionPool pool = CreatePool(new RabbitMQOptions { Hosts = ["localhost"] });
        PooledConnection pooled = CreatePooledConnection();
        pooled.SetBlocked(true, DateTimeOffset.UtcNow.AddMinutes(-2));
        pool.SeedSnapshotForTesting(pooled);

        int stalled = pool.EnforceBlockedQuarantine(TimeSpan.FromSeconds(60));

        Assert.That(stalled, Is.EqualTo(1));
        Assert.That(pooled.IsStalled, Is.True);
        Assert.That(pooled.TryAcquireSlot(), Is.False);
    }

    /// <summary>
    /// Verifies recent blocking does not quarantine the connection.
    /// </summary>
    [Test]
    public void EnforceBlockedQuarantine_WhenBlockedRecently_DoesNotMarkStalled()
    {
        ConnectionPool pool = CreatePool(new RabbitMQOptions { Hosts = ["localhost"] });
        PooledConnection pooled = CreatePooledConnection();
        pooled.SetBlocked(true);
        pool.SeedSnapshotForTesting(pooled);

        int stalled = pool.EnforceBlockedQuarantine(TimeSpan.FromSeconds(60));

        Assert.That(stalled, Is.EqualTo(0));
        Assert.That(pooled.IsStalled, Is.False);
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
