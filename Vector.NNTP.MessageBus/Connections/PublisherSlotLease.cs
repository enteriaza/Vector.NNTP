// <copyright file="PublisherSlotLease.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// PublisherSlotLease.cs -- IDisposable lease releasing one publisher slot on a PooledConnection.
//
// Acquired from ConnectionPool.AcquirePublisherSlotAsync and released in DisposeAsync, which decrements slot accounting
// and signals waiters. RabbitMqPublisherScope disposes the lease after closing its ephemeral channel.
//
// Thread safety:
//   DisposeAsync is idempotent via Interlocked; pool signals are thread-safe.

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Opaque lease for one publisher slot on a <see cref="PooledConnection"/>, returned by
    /// <see cref="ConnectionPool.AcquirePublisherSlotAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Ownership:</b> One lease maps to one increment of <see cref="PooledConnection.ActivePublisherSlots"/>.
    /// Disposing the lease releases the slot back to the pool.</para>
    /// <para><b>Lifecycle:</b></para>
    /// <list type="number">
    ///   <item><description>Created when <see cref="ConnectionPool"/> successfully acquires a slot.</description></item>
    ///   <item><description>Held for the lifetime of an <see cref="Publishing.IPublisherScope"/> (ephemeral channel).</description></item>
    ///   <item><description><see cref="DisposeAsync"/> releases the slot exactly once via <see cref="Interlocked"/>.</description></item>
    /// </list>
    /// <para><b>Thread safety:</b> Not thread-safe — a single scope/thread must own the lease. Concurrent dispose is
    /// idempotent (second dispose is a no-op).</para>
    /// </remarks>
    internal sealed class PublisherSlotLease : IAsyncDisposable
    {
        /// <summary>Pool that granted this lease.</summary>
        private readonly ConnectionPool _pool;

        /// <summary><c>0</c> = active, <c>1</c> = slot released.</summary>
        private int _released;

        /// <summary>Records a slot grant from <paramref name="pool"/> on <paramref name="connection"/>.</summary>
        /// <param name="pool">Owning connection pool.</param>
        /// <param name="connection">Connection that granted the slot permit.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pool"/> or <paramref name="connection"/> is null.</exception>
        internal PublisherSlotLease(ConnectionPool pool, PooledConnection connection)
        {
            ArgumentNullException.ThrowIfNull(pool);
            ArgumentNullException.ThrowIfNull(connection);
            _pool = pool;
            Connection = connection;
        }

        /// <summary>
        /// Pooled TCP connection that reserved one publisher slot for this lease.
        /// </summary>
        /// <remarks>Used by <see cref="Publishing.RabbitMqPublisherPool"/> to open an ephemeral <see cref="RabbitMQ.Client.IChannel"/>.</remarks>
        public PooledConnection Connection { get; }
        /// <summary>
        /// Releases the publisher slot back to <see cref="ConnectionPool"/> and signals waiting acquirers.
        /// </summary>
        /// <returns>A completed <see cref="ValueTask"/>.</returns>
        /// <remarks>
        /// <para><b>Idempotency:</b> Only the first dispose decrements slot accounting; subsequent calls are no-ops.</para>
        /// </remarks>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _pool.ReleaseSlot(Connection);
            return ValueTask.CompletedTask;
        }
    }
}
