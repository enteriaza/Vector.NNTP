// <copyright file="PooledConnectionTests.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>

using MessageBus.Connections;
using Moq;
using RabbitMQ.Client;

namespace MessageBus.Tests.Connections;

/// <summary>
/// Unit tests for <see cref="PooledConnection"/> slot and flow-control eligibility.
/// </summary>
[TestFixture]
public sealed class PooledConnectionTests
{
    /// <summary>
    /// Verifies blocked connections reject new publisher slots.
    /// </summary>
    [Test]
    public void TryAcquireSlot_WhenBlocked_ReturnsFalse()
    {
        PooledConnection pooled = CreateConnection();
        pooled.SetBlocked(true);

        Assert.That(pooled.TryAcquireSlot(), Is.False);
        Assert.That(pooled.ActivePublisherSlots, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies stalled connections reject new publisher slots.
    /// </summary>
    [Test]
    public void TryAcquireSlot_WhenStalled_ReturnsFalse()
    {
        PooledConnection pooled = CreateConnection();
        pooled.SetBlocked(true);
        pooled.SetStalled(true);

        Assert.That(pooled.TryAcquireSlot(), Is.False);
    }

    /// <summary>
    /// Verifies unblocking clears stalled quarantine and allows slots again.
    /// </summary>
    [Test]
    public void SetBlocked_WhenUnblocked_ClearsStalledAndAcceptsSlots()
    {
        PooledConnection pooled = CreateConnection();
        pooled.SetBlocked(true);
        pooled.SetStalled(true);
        pooled.SetBlocked(false);

        Assert.That(pooled.IsStalled, Is.False);
        Assert.That(pooled.TryAcquireSlot(), Is.True);
    }

    /// <summary>Creates a pooled connection backed by a mocked <see cref="IConnection"/>.</summary>
    /// <returns>Pooled connection entry.</returns>
    private static PooledConnection CreateConnection()
    {
        Mock<IConnection> connection = new();
        return new PooledConnection(Guid.NewGuid(), 0, connection.Object);
    }
}
