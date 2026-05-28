// <copyright file="PooledMultiplexer.cs" company="Usenet Ninja">
// Copyright (c) Chris Knipe &lt;cknipe@opticnetworks.net&gt;. Licensed under the Apache License, Version 2.0 (see LICENSE).
// </copyright>
using StackExchange.Redis;

namespace Vector.NNTP.Session.Redis.Connections
{
    /// <summary>
    /// A long-lived <see cref="IConnectionMultiplexer"/> entry managed by <see cref="RedisMultiplexerPool"/>.
    /// </summary>
    public sealed class PooledMultiplexer : IAsyncDisposable
    {
        /// <summary>
        /// Underlying Redis multiplexer.
        /// </summary>
        private IConnectionMultiplexer? _multiplexer;

        /// <summary>
        /// Initializes a new instance of the <see cref="PooledMultiplexer"/> class.
        /// </summary>
        /// <param name="connectionId">Stable pool-local identifier.</param>
        /// <param name="multiplexer">Connected Redis multiplexer.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="multiplexer"/> is null.</exception>
        internal PooledMultiplexer(Guid connectionId, IConnectionMultiplexer multiplexer)
        {
            ArgumentNullException.ThrowIfNull(multiplexer);
            ConnectionId = connectionId;
            _multiplexer = multiplexer;
            State = PooledMultiplexerState.Connected;
            ConnectedAtUtc = DateTimeOffset.UtcNow;
        }

        /// <summary>Gets stable pool-local identifier for logging.</summary>
        public Guid ConnectionId { get; }

        /// <summary>Gets underlying Redis multiplexer.</summary>
        /// <exception cref="InvalidOperationException">Thrown after disposal.</exception>
        public IConnectionMultiplexer Multiplexer =>
            _multiplexer ?? throw new InvalidOperationException("Multiplexer is disposed.");

        /// <summary>Gets current lifecycle state.</summary>
        public PooledMultiplexerState State { get; private set; }

        /// <summary>Gets UTC instant when the multiplexer was added to the pool.</summary>
        public DateTimeOffset ConnectedAtUtc { get; }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            IConnectionMultiplexer? multiplexer = Interlocked.Exchange(ref _multiplexer, null);
            if (multiplexer is not null)
            {
                await multiplexer.CloseAsync().ConfigureAwait(false);
                multiplexer.Dispose();
            }
        }

        /// <summary>Marks the entry faulted before removal from the snapshot.</summary>
        /// <param name="state">New lifecycle state.</param>
        internal void Transition(PooledMultiplexerState state)
        {
            State = state;
        }
    }
}
