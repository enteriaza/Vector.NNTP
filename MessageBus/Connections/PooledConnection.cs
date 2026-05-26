// <copyright file="PooledConnection.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
// PooledConnection.cs -- One long-lived AMQP TCP connection with publisher slot accounting and flow-control flags.
//
// Tracks active slot count, broker blocked/stalled state, and connection epoch for pool routing. Channels are opened by
// RabbitMqPublisherPool after slot acquisition, not by this type.
//
// Thread safety:
//   Slot and flag mutations use Interlocked and volatile reads; Connection reference set during pool add/remove.

using RabbitMQ.Client;

namespace MessageBus.Connections
{
    /// <summary>
    /// A long-lived TCP/AMQP connection entry managed by <see cref="ConnectionPool"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Responsibility:</b> Tracks publisher slot accounting, broker flow-control (blocked/stalled), and lifecycle
    /// state. Does not create channels — <see cref="Publishing.RabbitMqPublisherPool"/> opens ephemeral channels from
    /// <see cref="Connection"/>.</para>
    /// <para><b>Slot model:</b> Each concurrent <see cref="Publishing.IPublisherScope"/> holds one slot via
    /// <see cref="PublisherSlotLease"/>. <see cref="RabbitMQOptions.ChannelPoolSize"/> caps slots per TCP connection.</para>
    /// <para><b>Thread safety:</b> Slot counters and blocked flag use <see cref="Interlocked"/> /
    /// <see cref="Volatile"/>. <see cref="State"/> and <see cref="IsStalled"/> are updated from pool monitor paths and
    /// RabbitMQ client event handlers — callers must not mutate state outside those owners.</para>
    /// </remarks>
    public sealed class PooledConnection : IAsyncDisposable
    {
        /// <summary>Number of active publisher scopes holding a slot on this connection.</summary>
        private int _activePublisherSlots;

        /// <summary>Monotonic counter incremented on each <see cref="Transition"/>.</summary>
        private int _epoch;

        /// <summary><c>0</c> = not blocked, <c>1</c> = broker flow-control active.</summary>
        private int _blocked;

        /// <summary>Underlying AMQP connection; cleared on dispose.</summary>
        private IConnection? _connection;

        /// <summary>Initializes a new instance of the <see cref="PooledConnection"/> class.</summary>
        /// <param name="connectionId">Stable pool-local identifier for logging and fault handling.</param>
        /// <param name="hostIndex">Index into <see cref="RabbitMQOptions.Hosts"/> used for this TCP connection.</param>
        /// <param name="connection">Open AMQP connection from <see cref="RabbitMqConnectionFactory"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connection"/> is null.</exception>
        internal PooledConnection(Guid connectionId, int hostIndex, IConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ConnectionId = connectionId;
            HostIndex = hostIndex;
            _connection = connection;
            State = PooledConnectionState.Connected;
            ConnectedAtUtc = DateTimeOffset.UtcNow;
        }

        /// <summary>Stable pool-local identifier for this TCP connection.</summary>
        public Guid ConnectionId { get; }
        
        /// <summary>Index into <see cref="RabbitMQOptions.Hosts"/> selected when the connection was created.</summary>
        public int HostIndex { get; }

        /// <summary>Underlying AMQP connection used to create ephemeral publisher channels.</summary>
        /// <exception cref="InvalidOperationException">Thrown after <see cref="DisposeAsync"/>.</exception>
        public IConnection Connection => _connection ?? throw new InvalidOperationException("Connection is disposed.");

        /// <summary>Current lifecycle state in the pool finite-state machine.</summary>
        public PooledConnectionState State { get; private set; }
        /// <summary>Count of publisher slots currently held by active scopes.</summary>
        /// <remarks>Read via <see cref="Volatile.Read(ref int)"/> for cross-thread visibility.</remarks>
        public int ActivePublisherSlots => Volatile.Read(ref _activePublisherSlots);

        /// <summary>Monotonic epoch incremented on each <see cref="Transition"/>.</summary>
        /// <remarks>Consumers may compare epochs to detect state changes without polling <see cref="State"/>.</remarks>
        public int Epoch => Volatile.Read(ref _epoch);

        /// <summary>Whether the broker has activated connection-level flow control (<c>connection.blocked</c>).</summary>
        public bool IsBlocked => Volatile.Read(ref _blocked) != 0;

        /// <summary>
        /// Whether this connection is quarantined after remaining <see cref="IsBlocked"/> longer than
        /// <see cref="RabbitMQOptions.ConnectionBlockedTimeout"/>.
        /// </summary>
        /// <remarks>Stalled is not faulted — TCP remains open but new slots are refused until unblock.</remarks>
        public bool IsStalled { get; private set; }

        /// <summary>UTC time when this entry entered <see cref="PooledConnectionState.Connected"/>.</summary>
        public DateTimeOffset ConnectedAtUtc { get; }

        /// <summary>UTC time when the current block interval began, if <see cref="IsBlocked"/>.</summary>
        public DateTimeOffset? BlockedSinceUtc { get; private set; }

        /// <summary>
        /// Whether <see cref="ConnectionPool"/> may grant a new publisher slot on this connection.
        /// </summary>
        /// <remarks>
        /// Requires <see cref="PooledConnectionState.Connected"/> and neither <see cref="IsBlocked"/> nor
        /// <see cref="IsStalled"/>.
        /// </remarks>
        public bool CanAcceptNewSlots =>
            State == PooledConnectionState.Connected && !IsBlocked && !IsStalled;

        /// <summary>
        /// Attempts to reserve one publisher slot when <see cref="CanAcceptNewSlots"/> is true.
        /// </summary>
        /// <returns><see langword="true"/> when the slot counter was incremented.</returns>
        /// <remarks>
        /// <para>Uses optimistic increment with rollback if eligibility changes between check and increment (e.g. broker
        /// block on another thread).</para>
        /// </remarks>
        internal bool TryAcquireSlot()
        {
            if (!CanAcceptNewSlots)
                return false;
            _ = Interlocked.Increment(ref _activePublisherSlots);
            if (!CanAcceptNewSlots)
            {
                _ = Interlocked.Decrement(ref _activePublisherSlots);
                return false;
            }
            return true;
        }

        /// <summary>Releases one publisher slot previously acquired via <see cref="TryAcquireSlot"/>.</summary>
        /// <exception cref="InvalidOperationException">Thrown when <see cref="ActivePublisherSlots"/> would become negative.</exception>
        internal void ReleaseSlot()
        {
            int value = Interlocked.Decrement(ref _activePublisherSlots);
            if (value < 0)
                throw new InvalidOperationException("ActivePublisherSlots must not be negative.");
        }

        /// <summary>Updates broker flow-control blocked state from <see cref="IConnection.ConnectionBlockedAsync"/>.</summary>
        /// <param name="blocked"><see langword="true"/> when the broker blocked the connection.</param>
        /// <param name="blockedSinceUtc">
        /// UTC instant blocking began; defaults to <see cref="DateTimeOffset.UtcNow"/> when <paramref name="blocked"/> is
        /// <see langword="true"/>.
        /// </param>
        /// <remarks>Unblocking clears <see cref="IsStalled"/> automatically.</remarks>
        internal void SetBlocked(bool blocked, DateTimeOffset? blockedSinceUtc = null)
        {
            Volatile.Write(ref _blocked, blocked ? 1 : 0);
            BlockedSinceUtc = blocked ? blockedSinceUtc ?? DateTimeOffset.UtcNow : null;
            if (!blocked)
                IsStalled = false;
        }

        /// <summary>Sets prolonged-block quarantine from <see cref="ConnectionPool.EnforceBlockedQuarantine"/>.</summary>
        /// <param name="stalled"><see langword="true"/> to quarantine; <see langword="false"/> is not used (unblock clears stall).</param>
        internal void SetStalled(bool stalled)
        {
            IsStalled = stalled;
        }

        /// <summary>Transitions lifecycle <see cref="State"/> and bumps <see cref="Epoch"/>.</summary>
        /// <param name="newState">Target state.</param>
        internal void Transition(PooledConnectionState newState)
        {
            State = newState;
            _ = Interlocked.Increment(ref _epoch);
        }

        /// <summary>Closes and disposes the underlying <see cref="IConnection"/>.</summary>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
        /// <remarks><b>Idempotency:</b> Safe to call multiple times; only the first call closes TCP.</remarks>
        public async ValueTask DisposeAsync()
        {
            State = PooledConnectionState.Disposed;
            IConnection? connection = Interlocked.Exchange(ref _connection, null);
            if (connection is null)
                return;
            if (connection.IsOpen)
                await connection.CloseAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
