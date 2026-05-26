// <copyright file="ConnectionPool.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// ConnectionPool.cs -- Long-lived TCP connections and opaque publisher slot permits for MessageBus.
//
// Owns copy-on-write PooledConnection snapshots, slot acquisition with bounded waiters, and signals for scale-up and
// slot release. No AMQP channel or publisher-confirm knowledge — RabbitMqPublisherPool opens ephemeral channels after
// PublisherSlotLease acquisition.
//
// Thread safety:
//   Snapshot mutations under _snapshotLock; slot counts per connection via Interlocked; waiter queue via channels.
//
// Logging: [LoggerMessage] partial methods in ConnectionPool.Logging.cs.
//
// Cross-platform:
//   Portable BCL + RabbitMQ.Client; Windows x64 and Linux x64 on .NET 8.

using Vector.NNTP.MessageBus.Configuration;
using Vector.NNTP.MessageBus.Exceptions;
using RabbitMQ.Client;

namespace Vector.NNTP.MessageBus.Connections
{
    /// <summary>
    /// Manages long-lived TCP connections and opaque publisher slot permits for the MessageBus layer.
    /// </summary>
    /// <remarks>
    /// <para><b>Layering:</b> This type has no knowledge of publisher confirms, consumer tags, or AMQP channels. It only
    /// tracks TCP connections and slot counts. <see cref="Publishing.RabbitMqPublisherPool"/> opens ephemeral channels
    /// after acquiring a <see cref="PublisherSlotLease"/>.</para>
    /// <para><b>Snapshot model:</b> Active connections are stored in a copy-on-write <see cref="PooledConnection"/> array.
    /// Readers use <see cref="Volatile.Read(ref PooledConnection[])"/>; writers hold <see cref="_snapshotLock"/>.</para>
    /// <para><b>Waiters:</b> When no slot is immediately available, acquirers wait on <see cref="_slotReleasedSignal"/> until
    /// <see cref="RabbitMQOptions.ChannelLeaseTimeout"/>, bounded by <see cref="RabbitMQOptions.MaxPendingLeaseWaiters"/>.</para>
    /// <para><b>Thread safety:</b> Slot accounting is per-connection via <see cref="Interlocked"/>; snapshot mutations are
    /// lock-protected; waiter count uses <see cref="Interlocked"/> compare-exchange.</para>
    /// </remarks>
    public sealed partial class ConnectionPool : IAsyncDisposable
    {
        /// <summary>Factory used to open new AMQP TCP connections.</summary>
        private readonly RabbitMqConnectionFactory _connectionFactory;

        /// <summary>Validated RabbitMQ options snapshot.</summary>
        private readonly IOptions<RabbitMQOptions> _options;

        /// <summary>Logger for pool lifecycle events.</summary>
        private readonly ILogger<ConnectionPool> _logger;

        /// <summary>Serializes copy-on-write updates to <see cref="_snapshot"/>.</summary>
        private readonly object _snapshotLock = new();

        /// <summary>Coalesced scale-up signals consumed by <see cref="RabbitMqBackgroundScaler"/>.</summary>
        private readonly Channel<bool> _scaleUpSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        /// <summary>Signals slot release, new TCP capacity, or unblock so waiters retry acquisition.</summary>
        private readonly Channel<bool> _slotReleasedSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });

        /// <summary>Copy-on-write array of pooled connections visible to readers via <see cref="Volatile"/>.</summary>
        private PooledConnection[] _snapshot = [];

        /// <summary>Count of tasks blocked in <see cref="AcquirePublisherSlotAsync"/>.</summary>
        private int _pendingWaiters;

        /// <summary>When false, <see cref="AcquirePublisherSlotAsync"/> rejects new leases.</summary>
        private bool _acceptingSlots = true;

        /// <summary>Whether <see cref="DisposeAsync"/> has completed.</summary>
        private bool _disposed;

        /// <summary>Initializes a new instance of the <see cref="ConnectionPool"/> class.</summary>
        /// <param name="connectionFactory">Factory that creates <see cref="IConnection"/> instances.</param>
        /// <param name="options">Bound RabbitMQ options.</param>
        /// <param name="logger">Logger for connection add/remove events.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public ConnectionPool(
            RabbitMqConnectionFactory connectionFactory,
            IOptions<RabbitMQOptions> options,
            ILogger<ConnectionPool> logger)
        {
            ArgumentNullException.ThrowIfNull(connectionFactory);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);
            _connectionFactory = connectionFactory;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// Reader for coalesced scale-up signals consumed by <see cref="RabbitMqBackgroundScaler"/>.
        /// </summary>
        internal ChannelReader<bool> ScaleUpReader => _scaleUpSignal.Reader;

        /// <summary>
        /// Current copy-on-write snapshot of pooled connections.
        /// </summary>
        /// <remarks>May be empty before <see cref="StartAsync"/> completes.</remarks>
        public IReadOnlyList<PooledConnection> Snapshot => Volatile.Read(ref _snapshot);

        /// <summary>
        /// Opens <see cref="RabbitMQOptions.MinConnections"/> TCP connections.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for connection establishment.</param>
        /// <returns>A task representing the asynchronous start operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the pool has been disposed.</exception>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RabbitMQOptions options = _options.Value;
            for (int i = 0; i < options.MinConnections; i++)
                _ = await AddConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquires a publisher slot, waiting up to <see cref="RabbitMQOptions.ChannelLeaseTimeout"/>.
        /// </summary>
        /// <param name="cancellationToken">Caller cancellation token.</param>
        /// <returns>Lease bound to a <see cref="PooledConnection"/> that reserved one slot.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the pool has been disposed.</exception>
        /// <exception cref="MessageBusUnavailableException">
        /// Thrown when the pool is not accepting slots or <see cref="RabbitMQOptions.MaxPendingLeaseWaiters"/> is exceeded.
        /// </exception>
        /// <exception cref="MessageBusLeaseTimeoutException">
        /// Thrown when no slot becomes available before <see cref="RabbitMQOptions.ChannelLeaseTimeout"/>.
        /// </exception>
        /// <remarks>
        /// <para><b>Acquisition loop:</b></para>
        /// <list type="number">
        ///   <item><description>Try immediate slot on a lightly loaded connection.</description></item>
        ///   <item><description>Signal <see cref="RabbitMqBackgroundScaler"/> to add TCP if at capacity.</description></item>
        ///   <item><description>Wait on <see cref="_slotReleasedSignal"/> until deadline or cancellation.</description></item>
        /// </list>
        /// </remarks>
        public async Task<PublisherSlotLease> AcquirePublisherSlotAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_acceptingSlots)
                throw new MessageBusUnavailableException("Connection pool is not accepting new publisher slots.");
            RabbitMQOptions options = _options.Value;
            if (!TryEnterWaitQueue(options.MaxPendingLeaseWaiters))
                throw new MessageBusUnavailableException("Publisher slot waiter queue is full.");
            try
            {
                DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(options.ChannelLeaseTimeout);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryAcquireSlotImmediate(options, out PooledConnection? connection) && connection is not null)
                        return new PublisherSlotLease(this, connection);
                    SignalScaleUp();
                    TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linked.CancelAfter(remaining);
                    try
                    {
                        if (await _slotReleasedSignal.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                            _ = await _slotReleasedSignal.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                throw new MessageBusLeaseTimeoutException(
                    $"Timed out waiting for a publisher slot after {options.ChannelLeaseTimeout}.");
            }
            finally
            {
                ExitWaitQueue();
            }
        }

        /// <summary>
        /// Releases a publisher slot and wakes waiters.
        /// </summary>
        /// <param name="connection">Connection that held the slot.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connection"/> is null.</exception>
        internal void ReleaseSlot(PooledConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            connection.ReleaseSlot();
            _ = _slotReleasedSignal.Writer.TryWrite(true);
        }

        /// <summary>Coalesces a scale-up request to <see cref="RabbitMqBackgroundScaler"/>.</summary>
        internal void SignalScaleUp()
        {
            _ = _scaleUpSignal.Writer.TryWrite(true);
        }

        /// <summary>
        /// Creates a new <see cref="PooledConnection"/>, attaches flow-control handlers, and appends to the snapshot.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for TCP connect.</param>
        /// <returns>The new pooled entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when factory returns null (should not occur).</exception>
        internal async Task<PooledConnection> AddConnectionAsync(CancellationToken cancellationToken)
        {
            RabbitMQOptions options = _options.Value;
            int hostIndex = Random.Shared.Next(options.Hosts.Length);
            IConnection connection = await _connectionFactory.CreateConnectionAsync(options, cancellationToken)
                .ConfigureAwait(false);
            PooledConnection pooled = new(Guid.NewGuid(), hostIndex, connection);
            AttachFlowControlHandlers(connection, pooled);
            AppendToSnapshot(pooled);
            LogConnectionAdded(pooled.ConnectionId, hostIndex);
            _ = _slotReleasedSignal.Writer.TryWrite(true);
            return pooled;
        }

        /// <summary>
        /// Marks a connection <see cref="PooledConnectionState.Faulted"/> and removes it from the active snapshot.
        /// </summary>
        /// <param name="connectionId">Identifier of the faulted connection.</param>
        internal void MarkFaulted(Guid connectionId)
        {
            PooledConnection[] current = Volatile.Read(ref _snapshot);
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].ConnectionId != connectionId)
                    continue;
                current[i].Transition(PooledConnectionState.Faulted);
                RemoveFromSnapshot(connectionId);
                return;
            }
        }

        /// <summary>
        /// Returns total publisher slot capacity across connections that can accept slots.
        /// </summary>
        /// <returns>Eligible connection count multiplied by <see cref="RabbitMQOptions.ChannelPoolSize"/>.</returns>
        internal int GetUsableSlotCapacity()
        {
            RabbitMQOptions options = _options.Value;
            int usableConnections = CountEligibleConnections();
            return usableConnections * options.ChannelPoolSize;
        }

        /// <summary>
        /// Quarantines connections blocked longer than <paramref name="blockedTimeout"/> as
        /// <see cref="PooledConnection.IsStalled"/>.
        /// </summary>
        /// <param name="blockedTimeout">Maximum blocked duration before quarantine.</param>
        /// <returns>Number of connections newly quarantined in this scan.</returns>
        /// <remarks>Invoked by <see cref="RabbitMqPoolFlowControlMonitor"/>.</remarks>
        internal int EnforceBlockedQuarantine(TimeSpan blockedTimeout)
        {
            if (blockedTimeout <= TimeSpan.Zero)
                return 0;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PooledConnection[] snapshot = Volatile.Read(ref _snapshot);
            int stalledCount = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                PooledConnection pooled = snapshot[i];
                if (!pooled.IsBlocked || pooled.IsStalled || pooled.BlockedSinceUtc is not DateTimeOffset blockedSince)
                    continue;
                if (now - blockedSince < blockedTimeout)
                    continue;
                pooled.SetStalled(true);
                stalledCount++;
            }
            if (stalledCount > 0)
                _ = _slotReleasedSignal.Writer.TryWrite(true);
            return stalledCount;
        }

        /// <summary>
        /// Replaces the active snapshot for unit tests without disposing prior entries.
        /// </summary>
        /// <param name="connections">Connections to publish as the new snapshot.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connections"/> is null.</exception>
        internal void SeedSnapshotForTesting(params PooledConnection[] connections)
        {
            ArgumentNullException.ThrowIfNull(connections);
            PublishSnapshot(connections);
        }

        /// <summary>
        /// Sums <see cref="PooledConnection.ActivePublisherSlots"/> on connections that can still accept slots.
        /// </summary>
        /// <returns>Active slot count used for scale-up decisions.</returns>
        internal int GetUsedSlotCount()
        {
            PooledConnection[] snapshot = Volatile.Read(ref _snapshot);
            int used = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].CanAcceptNewSlots)
                    used += snapshot[i].ActivePublisherSlots;
            }
            return used;
        }

        /// <summary>
        /// Stops accepting slots, completes signal channels, and disposes all pooled connections.
        /// </summary>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;
            _acceptingSlots = false;
            _ = _scaleUpSignal.Writer.TryComplete();
            _ = _slotReleasedSignal.Writer.TryComplete();
            PooledConnection[] snapshot = Volatile.Read(ref _snapshot);
            for (int i = 0; i < snapshot.Length; i++)
                await snapshot[i].DisposeAsync().ConfigureAwait(false);
            PublishSnapshot([]);
        }

        /// <summary>
        /// Attempts to increment <see cref="_pendingWaiters"/> when below <paramref name="maxWaiters"/>.
        /// </summary>
        /// <param name="maxWaiters">Configured <see cref="RabbitMQOptions.MaxPendingLeaseWaiters"/>.</param>
        /// <returns><see langword="true"/> when the caller may enter the wait loop.</returns>
        private bool TryEnterWaitQueue(int maxWaiters)
        {
            for (; ; )
            {
                int current = Volatile.Read(ref _pendingWaiters);
                if (current >= maxWaiters)
                    return false;
                if (Interlocked.CompareExchange(ref _pendingWaiters, current + 1, current) == current)
                    return true;
            }
        }

        /// <summary>Decrements <see cref="_pendingWaiters"/> after acquire completes or fails.</summary>
        private void ExitWaitQueue()
        {
            _ = Interlocked.Decrement(ref _pendingWaiters);
        }

        /// <summary>
        /// Attempts to acquire a slot on a lightly loaded connection using random pairwise comparison.
        /// </summary>
        /// <param name="options">Options providing <see cref="RabbitMQOptions.ChannelPoolSize"/>.</param>
        /// <param name="connection">Connection that acquired the slot, if successful.</param>
        /// <returns><see langword="true"/> when a slot was reserved.</returns>
        private bool TryAcquireSlotImmediate(RabbitMQOptions options, out PooledConnection? connection)
        {
            connection = null;
            PooledConnection[] snapshot = Volatile.Read(ref _snapshot);
            int length = snapshot.Length;
            if (length == 0)
                return false;
            int attempts = Math.Min(4, length * 2);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                PooledConnection first = snapshot[Random.Shared.Next(length)];
                PooledConnection second = snapshot[Random.Shared.Next(length)];
                PooledConnection chosen = first.ActivePublisherSlots <= second.ActivePublisherSlots ? first : second;
                if (!chosen.CanAcceptNewSlots || chosen.ActivePublisherSlots >= options.ChannelPoolSize)
                    continue;
                if (chosen.TryAcquireSlot())
                {
                    connection = chosen;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Counts snapshot entries where <see cref="PooledConnection.CanAcceptNewSlots"/> is true.</summary>
        /// <returns>Number of connections eligible for new slots.</returns>
        private int CountEligibleConnections()
        {
            PooledConnection[] snapshot = Volatile.Read(ref _snapshot);
            int count = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].CanAcceptNewSlots)
                    count++;
            }
            return count;
        }

        /// <summary>Appends <paramref name="pooled"/> to the snapshot under <see cref="_snapshotLock"/>.</summary>
        /// <param name="pooled">Connection to add.</param>
        private void AppendToSnapshot(PooledConnection pooled)
        {
            lock (_snapshotLock)
            {
                PooledConnection[] current = _snapshot;
                PooledConnection[] next = new PooledConnection[current.Length + 1];
                current.AsSpan().CopyTo(next);
                next[current.Length] = pooled;
                Volatile.Write(ref _snapshot, next);
            }
        }

        /// <summary>Removes the connection identified by <paramref name="connectionId"/> from the snapshot.</summary>
        /// <param name="connectionId">Connection to remove.</param>
        private void RemoveFromSnapshot(Guid connectionId)
        {
            lock (_snapshotLock)
            {
                PooledConnection[] current = _snapshot;
                int remaining = 0;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i].ConnectionId != connectionId)
                        remaining++;
                }
                if (remaining == current.Length)
                    return;
                PooledConnection[] next = new PooledConnection[remaining];
                int index = 0;
                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i].ConnectionId == connectionId)
                        continue;
                    next[index++] = current[i];
                }
                Volatile.Write(ref _snapshot, next);
            }
        }

        /// <summary>Publishes <paramref name="snapshot"/> as the current reader-visible array.</summary>
        /// <param name="snapshot">New snapshot array.</param>
        private void PublishSnapshot(PooledConnection[] snapshot)
        {
            lock (_snapshotLock)
                Volatile.Write(ref _snapshot, snapshot);
        }

        /// <summary>
        /// Subscribes to broker <c>connection.blocked</c> / <c>connection.unblocked</c> on <paramref name="connection"/>.
        /// </summary>
        /// <param name="connection">AMQP connection.</param>
        /// <param name="pooled">Pooled entry to update.</param>
        private void AttachFlowControlHandlers(IConnection connection, PooledConnection pooled)
        {
            connection.ConnectionBlockedAsync += (_, _) =>
            {
                pooled.SetBlocked(true);
                return Task.CompletedTask;
            };
            connection.ConnectionUnblockedAsync += (_, _) =>
            {
                pooled.SetBlocked(false);
                _ = _slotReleasedSignal.Writer.TryWrite(true);
                return Task.CompletedTask;
            };
        }
    }
}
